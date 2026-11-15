using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using System;
using System.Diagnostics;
using System.Linq;

namespace Piece_Launcher.Controls
{
    public sealed class HatSpinner : Control
    {
        private const int FrameCount = 48;
        private const int Fps = 48;

        private static Bitmap[]? _frames;
        private readonly DispatcherTimer _timer = new()
        {
            Interval = TimeSpan.FromSeconds(1.0 / Fps)
        };

        private int _index;

        public HatSpinner()
        {
            RenderOptions.SetBitmapInterpolationMode(this, BitmapInterpolationMode.HighQuality);
            _timer.Tick += OnTimerTick;
        }

        private static Bitmap[] LoadFrames()
        {
            try
            {
                return Enumerable.Range(0, FrameCount)
                    .Select(i =>
                    {
                        var uri = new Uri($"avares://Piece%20Launcher/Assets/Loading/hat_{i:00}.png");
                        if (!AssetLoader.Exists(uri))
                            uri = new Uri($"avares://Piece Launcher/Assets/Loading/hat_{i:00}.png");
                        return new Bitmap(AssetLoader.Open(uri));
                    })
                    .ToArray();
            }
            catch (Exception exception)
            {
                Trace.WriteLine($"Could not load the hat spinner frames: {exception}");
                return Array.Empty<Bitmap>();
            }
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            _frames ??= LoadFrames();
            _timer.Start();
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            _timer.Stop();
            base.OnDetachedFromVisualTree(e);
        }

        private void OnTimerTick(object? sender, EventArgs e)
        {
            if (_frames is not { Length: > 0 })
                return;

            _index = (_index + 1) % _frames.Length;
            InvalidateVisual();
        }

        public override void Render(DrawingContext context)
        {
            if (_frames is null || _frames.Length == 0)
                return;

            var frame = _frames[_index % _frames.Length];
            var side = Math.Min(Bounds.Width, Bounds.Height);
            if (side <= 0)
                return;

            var dest = new Rect((Bounds.Width - side) / 2, (Bounds.Height - side) / 2, side, side);
            context.DrawImage(frame, new Rect(frame.Size), dest);
        }
    }
}
