using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using ClaudeVisualChat.Sample;
using Xunit;

namespace ClaudeVisualChat.Tests;

/// <summary>
/// The sample's player: it feeds a recording to the conversation line by line and stops at each permission request
/// until the request leaves the queue, however it leaves.
/// </summary>
public class FixturePlayerTests
{
    private static readonly TimeSpan Wait = TimeSpan.FromMilliseconds(200);
    private static readonly TimeSpan Long = TimeSpan.FromSeconds(10);

    private static ChatConversation Direct(string scenario)
    {
        var conversation = new ChatConversation();
        foreach (var line in FixtureFiles.StreamLines(scenario)) conversation.AppendLine(line);
        return conversation;
    }

    private static Task Play(string scenario, ChatConversation conversation, CancellationToken ct = default) =>
        new FixturePlayer(FixtureFiles.StreamLines(scenario), conversation).PlayAsync(TimeSpan.Zero, TimeSpan.Zero, ct);

    /// <summary>Runs the UI thread's jobs until the task ends or the time is up; <paramref name="step"/> runs each round.</summary>
    private static void RunUntil(Task task, TimeSpan timeout, Action? step = null)
    {
        var watch = Stopwatch.StartNew();
        while (!task.IsCompleted && watch.Elapsed < timeout)
        {
            Dispatcher.UIThread.RunJobs();
            step?.Invoke();
            Thread.Sleep(1);
        }
        Dispatcher.UIThread.RunJobs();
    }

    private static void AnswerYes(ChatConversation conversation)
    {
        if (conversation.CurrentPermission is { } request) conversation.AnswerPermission(request, PermissionDecision.Allow);
    }

    private static IEnumerable<string> Kinds(ChatConversation conversation) =>
        conversation.Items.Select(i => i.GetType().Name);

    [AvaloniaFact]
    public void Answering_each_request_plays_the_whole_recording()
    {
        var played = new ChatConversation();
        var answered = new List<string>();
        played.PermissionAnswered += (_, e) => answered.Add(e.Request.ToolName);

        var task = Play("edit", played);
        RunUntil(task, Long, () => AnswerYes(played));

        Assert.True(task.IsCompletedSuccessfully);
        Assert.Equal(new[] { "Edit", "Write" }, answered);
        var direct = Direct("edit");
        Assert.Equal(direct.Items.Count, played.Items.Count);
        Assert.Equal(Kinds(direct), Kinds(played));
        Assert.Empty(played.PendingPermissions);
        Assert.False(played.IsBusy);
    }

    [AvaloniaFact]
    public void Without_an_answer_the_player_waits_at_the_first_request()
    {
        var played = new ChatConversation();
        var task = Play("edit", played);
        RunUntil(task, Wait);

        Assert.False(task.IsCompleted);
        Assert.Equal("Edit", played.CurrentPermission?.ToolName);
        Assert.Single(played.PendingPermissions);
        Assert.True(played.Items.Count < Direct("edit").Items.Count);
    }

    [AvaloniaFact]
    public void A_request_that_leaves_the_queue_another_way_lets_the_player_go_on()
    {
        var played = new ChatConversation();
        var answered = 0;
        played.PermissionAnswered += (_, _) => answered++;
        var task = Play("edit", played);
        RunUntil(task, Wait);

        played.DismissPermission(played.CurrentPermission!.RequestId); // answered somewhere else
        RunUntil(task, Wait);
        Assert.False(task.IsCompleted);
        var write = played.CurrentPermission!;
        Assert.Equal("Write", write.ToolName); // on to the next request

        played.DismissPermission(write.RequestId);
        played.AnswerPermission(write, PermissionDecision.Allow); // too late: out of the queue, no event
        RunUntil(task, Long);

        Assert.True(task.IsCompletedSuccessfully);
        Assert.Equal(0, answered);
        Assert.Equal(Kinds(Direct("edit")), Kinds(played));
    }

    [AvaloniaFact]
    public void Cancelling_stops_the_player_where_it_is()
    {
        using var cts = new CancellationTokenSource();
        var played = new ChatConversation();
        var task = Play("edit", played, cts.Token);
        RunUntil(task, Wait);
        var items = played.Items.Count;

        cts.Cancel();
        RunUntil(task, Long);
        Assert.True(task.IsCanceled);

        played.DismissPermission(played.CurrentPermission!.RequestId); // nothing left to wake up
        RunUntil(task, Wait);
        Assert.Equal(items, played.Items.Count);
    }

    [AvaloniaFact]
    public void Stream_events_and_other_lines_each_wait_their_own_interval()
    {
        var lines = FixtureFiles.StreamLines("error");
        var firstStreamEvent = Array.FindIndex(lines, l =>
            JsonDocument.Parse(l).RootElement.GetProperty("type").GetString() == "stream_event");
        Assert.True(firstStreamEvent > 0);

        using var cts = new CancellationTokenSource();
        var slowEvents = new FixturePlayer(lines, new ChatConversation());
        var slowOthers = new FixturePlayer(lines, new ChatConversation());
        var first = slowEvents.PlayAsync(TimeSpan.FromHours(1), TimeSpan.Zero, cts.Token);
        var second = slowOthers.PlayAsync(TimeSpan.Zero, TimeSpan.FromHours(1), cts.Token);
        RunUntil(Task.WhenAll(first, second), Wait);

        Assert.Equal(firstStreamEvent + 1, slowEvents.Position); // stopped after the first stream event
        Assert.Equal(1, slowOthers.Position);                    // stopped after the init
        cts.Cancel();
        RunUntil(Task.WhenAll(first, second).ContinueWith(_ => { }, TaskScheduler.Default), Long);
        Assert.True(first.IsCanceled && second.IsCanceled);
    }

    [AvaloniaFact]
    public void The_recording_time_follows_the_timestamps_of_the_lines()
    {
        var played = new ChatConversation();
        var player = new FixturePlayer(FixtureFiles.StreamLines("edit"), played) { Prompts = new[] { "go" } };
        Assert.Null(player.RecordingTime);
        played.Clock = () => player.RecordingTime ?? DateTimeOffset.MinValue;

        var task = player.PlayAsync(TimeSpan.Zero, TimeSpan.Zero, CancellationToken.None);
        RunUntil(task, Long, () => AnswerYes(played));

        Assert.True(task.IsCompletedSuccessfully);
        // the message goes in before any line with a time: it gets the recording's first one
        Assert.Equal(DateTimeOffset.Parse("2026-10-19T21:31:35.778Z"), played.Items.OfType<UserMessageItem>().Single().Timestamp);
        Assert.Equal(DateTimeOffset.Parse("2026-10-19T21:31:44.016Z"), player.RecordingTime);
        // so the thoughts last what they lasted in the recording, not what the playing took
        Assert.Equal(new long?[] { 0, 2017, 5242 }, played.Items.OfType<ThinkingItem>().Select(t => t.DurationMs));
    }

    [AvaloniaFact]
    public void The_users_messages_go_in_at_the_start_of_each_turn()
    {
        // mode-and-model has two turns; the claude does not repeat what the user sent, so the player submits it
        var played = new ChatConversation();
        var sent = new List<string>();
        played.MessageSubmitted += (_, e) => sent.Add(e.Text);
        var player = new FixturePlayer(FixtureFiles.StreamLines("mode-and-model"), played)
        {
            Prompts = new[] { "first", "second" },
        };

        var task = player.PlayAsync(TimeSpan.Zero, TimeSpan.Zero, CancellationToken.None);
        RunUntil(task, Long, () => AnswerYes(played));

        Assert.True(task.IsCompletedSuccessfully);
        Assert.Equal(new[] { "first", "second" }, sent);
        var items = played.Items.ToList();
        Assert.Equal("first", Assert.IsType<UserMessageItem>(items[0]).Text);
        var second = items.FindIndex(i => i is UserMessageItem { Text: "second" });
        var firstFooter = items.FindIndex(i => i is TurnFooterItem);
        Assert.True(second > firstFooter && firstFooter > 0, "the second message starts the second turn");
        Assert.Equal(Direct("mode-and-model").Items.Count + 2, items.Count);
    }

    [AvaloniaTheory]
    [InlineData("image")]
    [InlineData("commands")]
    [InlineData("effort")]
    public void The_new_recordings_play_to_the_end(string scenario)
    {
        var played = new ChatConversation();
        var player = new FixturePlayer(FixtureFiles.StreamLines(scenario), played);

        var task = player.PlayAsync(TimeSpan.Zero, TimeSpan.Zero, CancellationToken.None);
        RunUntil(task, Long, () => AnswerYes(played));

        Assert.True(task.IsCompletedSuccessfully);
        Assert.Equal(FixtureFiles.StreamLines(scenario).Length, player.Position);
        Assert.Equal(Kinds(Direct(scenario)), Kinds(played));
        Assert.Empty(played.PendingPermissions);
    }
}
