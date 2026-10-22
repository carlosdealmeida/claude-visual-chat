using System;
using ClaudeVisualChat.Imaging;

namespace ClaudeVisualChat
{
    /// <summary>An image attached to a message: its media type (<c>image/png</c>) and its bytes in base64.</summary>
    public sealed class ChatImage
    {
        private int _width = -1;
        private int _height;

        public ChatImage(string mediaType, string data)
        {
            MediaType = mediaType ?? throw new ArgumentNullException(nameof(mediaType));
            Data = data ?? throw new ArgumentNullException(nameof(data));
        }

        private ChatImage(string mediaType, string data, int width, int height)
            : this(mediaType, data)
        {
            _width = width;
            _height = height;
        }

        public string MediaType { get; }

        /// <summary>The image bytes, base64 encoded.</summary>
        public string Data { get; }

        /// <summary>Width in pixels, read from the image header; 0 when it cannot be read.</summary>
        public int Width
        {
            get
            {
                Measure();
                return _width;
            }
        }

        /// <summary>Height in pixels, read from the image header; 0 when it cannot be read.</summary>
        public int Height
        {
            get
            {
                Measure();
                return _height;
            }
        }

        /// <summary>Wraps encoded image bytes (PNG, JPEG…) of the given media type.</summary>
        public static ChatImage FromBytes(byte[] encoded, string mediaType)
        {
            if (encoded == null) throw new ArgumentNullException(nameof(encoded));
            var size = ImageFitter.Measure(encoded);
            return new ChatImage(mediaType, Convert.ToBase64String(encoded), size?.Width ?? 0, size?.Height ?? 0);
        }

        /// <summary>The encoded image bytes; empty when <see cref="Data"/> is not valid base64.</summary>
        public byte[] ToBytes()
        {
            try
            {
                return Convert.FromBase64String(Data);
            }
            catch (FormatException)
            {
                return Array.Empty<byte>();
            }
        }

        private void Measure()
        {
            if (_width >= 0) return;
            var size = ImageFitter.Measure(ToBytes());
            _height = size?.Height ?? 0;
            _width = size?.Width ?? 0;
        }
    }

    /// <summary>What <see cref="ChatConversation.AddImageAsync"/> did with the bytes.</summary>
    public enum AddImageResult
    {
        Added,
        TooMany,
        TooBig,
        NotAnImage,
    }
}
