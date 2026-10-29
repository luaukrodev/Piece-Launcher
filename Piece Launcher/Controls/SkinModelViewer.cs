using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Piece_Launcher.Controls
{
    public sealed class SkinModelViewer : Control
    {
        public static readonly StyledProperty<Bitmap?> SkinBitmapProperty =
            AvaloniaProperty.Register<SkinModelViewer, Bitmap?>(nameof(SkinBitmap));

        private readonly Dictionary<uint, IBrush> _brushes = new();
        private byte[]? _pixels;
        private int _skinWidth;
        private int _skinHeight;
        private double _rotation = -0.45;
        private Point? _lastPointerPosition;

        static SkinModelViewer()
        {
            SkinBitmapProperty.Changed.AddClassHandler<SkinModelViewer>(
                (viewer, _) => viewer.LoadSkinPixels());
        }

        public Bitmap? SkinBitmap
        {
            get => GetValue(SkinBitmapProperty);
            set => SetValue(SkinBitmapProperty, value);
        }

        public SkinModelViewer()
        {
            ClipToBounds = true;
            RenderOptions.SetEdgeMode(this, EdgeMode.Aliased);
            PointerPressed += OnPointerPressed;
            PointerMoved += OnPointerMoved;
            PointerReleased += OnPointerReleased;
        }

        public override void Render(DrawingContext context)
        {
            base.Render(context);
            if (_pixels == null || Bounds.Width <= 0 || Bounds.Height <= 0)
                return;

            var cells = new List<RenderCell>(1800);

            AddBox(cells, new Point3(0, -12, 0), 8, 8, 8,
                8, 8, 24, 8, 16, 8, 0, 8, 8, 0, 16, 0);
            AddBox(cells, new Point3(0, -2, 0), 8, 12, 4,
                20, 20, 32, 20, 28, 20, 16, 20, 20, 16, 28, 16);
            AddBox(cells, new Point3(-6, -2, 0), 4, 12, 4,
                44, 20, 52, 20, 40, 20, 48, 20, 44, 16, 48, 16);
            AddBox(cells, new Point3(6, -2, 0), 4, 12, 4,
                44, 20, 52, 20, 40, 20, 48, 20, 44, 16, 48, 16);
            AddBox(cells, new Point3(-2, 10, 0), 4, 12, 4,
                4, 20, 12, 20, 0, 20, 8, 20, 4, 16, 8, 16);
            if (_skinHeight == 64)
                AddBox(cells, new Point3(2, 10, 0), 4, 12, 4,
                    20, 52, 28, 52, 24, 52, 16, 52, 20, 48, 24, 48);
            else
                AddBox(cells, new Point3(2, 10, 0), 4, 12, 4,
                    4, 20, 12, 20, 0, 20, 8, 20, 4, 16, 8, 16);

                if (_skinHeight == 64)
                {
                    AddBox(cells, new Point3(0, -12, 0), 8, 8, 8,
                        40, 8, 56, 8, 48, 8, 32, 8, 40, 0, 48, 0, 0.5);
                    AddBox(cells, new Point3(0, -2, 0), 8, 12, 4,
                        20, 36, 32, 36, 16, 36, 28, 36, 20, 32, 28, 32, 0.25);
                    AddBox(cells, new Point3(-6, -2, 0), 4, 12, 4,
                        44, 36, 52, 36, 40, 36, 48, 36, 44, 32, 48, 32, 0.25);
                    AddBox(cells, new Point3(6, -2, 0), 4, 12, 4,
                        36, 52, 44, 52, 32, 52, 40, 52, 36, 48, 40, 48, 0.25);
                    AddBox(cells, new Point3(-2, 10, 0), 4, 12, 4,
                        4, 36, 12, 36, 0, 36, 8, 36, 4, 32, 8, 32, 0.25);
                    AddBox(cells, new Point3(2, 10, 0), 4, 12, 4,
                        4, 52, 12, 52, 0, 52, 8, 52, 4, 48, 8, 48, 0.25);
                }

                cells.Sort((left, right) => left.Depth.CompareTo(right.Depth));
            _brushes.Clear();

            foreach (var cell in cells)
            {
                var brush = GetBrush(cell.Color);
                var geometry = new StreamGeometry();
                using (var path = geometry.Open())
                {
                    path.BeginFigure(cell.TopLeft, true);
                    path.LineTo(cell.TopRight);
                    path.LineTo(cell.BottomRight);
                    path.LineTo(cell.BottomLeft);
                    path.EndFigure(true);
                }
                context.DrawGeometry(brush, null, geometry);
            }
        }

        private void LoadSkinPixels()
        {
            _pixels = null;
            var bitmap = SkinBitmap;
            if (bitmap == null || bitmap.PixelSize.Width != 64 || bitmap.PixelSize.Height is not (32 or 64))
            {
                InvalidateVisual();
                return;
            }

            _skinWidth = bitmap.PixelSize.Width;
            _skinHeight = bitmap.PixelSize.Height;
            using var staging = new WriteableBitmap(
                bitmap.PixelSize, bitmap.Dpi, PixelFormats.Bgra8888, AlphaFormat.Unpremul);
            using var framebuffer = staging.Lock();
            bitmap.CopyPixels(framebuffer);
            _pixels = new byte[framebuffer.RowBytes * _skinHeight];
            Marshal.Copy(framebuffer.Address, _pixels, 0, _pixels.Length);
            InvalidateVisual();
        }

        private void AddBox(
            List<RenderCell> cells,
            Point3 center,
            int width,
            int height,
            int depth,
            int frontU,
            int frontV,
            int backU,
            int backV,
            int sideU,
            int sideV,
            int oppositeSideU,
            int oppositeSideV,
            int topU,
            int topV,
            int bottomU,
            int bottomV,
            double expansion = 0)
        {
            var boxWidth = width + expansion * 2;
            var boxHeight = height + expansion * 2;
            var boxDepth = depth + expansion * 2;
            var stepX = boxWidth / width;
            var stepY = boxHeight / height;
            var stepZ = boxDepth / depth;

            for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                AddCell(cells,
                    new Point3(center.X - boxWidth / 2 + x * stepX, center.Y - boxHeight / 2 + y * stepY, center.Z + boxDepth / 2),
                    new Point3(center.X - boxWidth / 2 + (x + 1) * stepX, center.Y - boxHeight / 2 + y * stepY, center.Z + boxDepth / 2),
                    new Point3(center.X - boxWidth / 2 + (x + 1) * stepX, center.Y - boxHeight / 2 + (y + 1) * stepY, center.Z + boxDepth / 2),
                    new Point3(center.X - boxWidth / 2 + x * stepX, center.Y - boxHeight / 2 + (y + 1) * stepY, center.Z + boxDepth / 2),
                    frontU + x, frontV + y);
                AddCell(cells,
                    new Point3(center.X + boxWidth / 2 - x * stepX, center.Y - boxHeight / 2 + y * stepY, center.Z - boxDepth / 2),
                    new Point3(center.X + boxWidth / 2 - (x + 1) * stepX, center.Y - boxHeight / 2 + y * stepY, center.Z - boxDepth / 2),
                    new Point3(center.X + boxWidth / 2 - (x + 1) * stepX, center.Y - boxHeight / 2 + (y + 1) * stepY, center.Z - boxDepth / 2),
                    new Point3(center.X + boxWidth / 2 - x * stepX, center.Y - boxHeight / 2 + (y + 1) * stepY, center.Z - boxDepth / 2),
                    backU + x, backV + y);
            }

            for (var y = 0; y < height; y++)
            for (var z = 0; z < depth; z++)
            {
                AddCell(cells,
                    new Point3(center.X + boxWidth / 2, center.Y - boxHeight / 2 + y * stepY, center.Z + boxDepth / 2 - z * stepZ),
                    new Point3(center.X + boxWidth / 2, center.Y - boxHeight / 2 + y * stepY, center.Z + boxDepth / 2 - (z + 1) * stepZ),
                    new Point3(center.X + boxWidth / 2, center.Y - boxHeight / 2 + (y + 1) * stepY, center.Z + boxDepth / 2 - (z + 1) * stepZ),
                    new Point3(center.X + boxWidth / 2, center.Y - boxHeight / 2 + (y + 1) * stepY, center.Z + boxDepth / 2 - z * stepZ),
                    sideU + z, sideV + y);
                AddCell(cells,
                    new Point3(center.X - boxWidth / 2, center.Y - boxHeight / 2 + y * stepY, center.Z - boxDepth / 2 + (z + 1) * stepZ),
                    new Point3(center.X - boxWidth / 2, center.Y - boxHeight / 2 + y * stepY, center.Z - boxDepth / 2 + z * stepZ),
                    new Point3(center.X - boxWidth / 2, center.Y - boxHeight / 2 + (y + 1) * stepY, center.Z - boxDepth / 2 + z * stepZ),
                    new Point3(center.X - boxWidth / 2, center.Y - boxHeight / 2 + (y + 1) * stepY, center.Z - boxDepth / 2 + (z + 1) * stepZ),
                    oppositeSideU + z, oppositeSideV + y);
            }

            for (var z = 0; z < depth; z++)
            for (var x = 0; x < width; x++)
            {
                AddCell(cells,
                    new Point3(center.X - boxWidth / 2 + x * stepX, center.Y - boxHeight / 2, center.Z + boxDepth / 2 - z * stepZ),
                    new Point3(center.X - boxWidth / 2 + (x + 1) * stepX, center.Y - boxHeight / 2, center.Z + boxDepth / 2 - z * stepZ),
                    new Point3(center.X - boxWidth / 2 + (x + 1) * stepX, center.Y - boxHeight / 2, center.Z + boxDepth / 2 - (z + 1) * stepZ),
                    new Point3(center.X - boxWidth / 2 + x * stepX, center.Y - boxHeight / 2, center.Z + boxDepth / 2 - (z + 1) * stepZ),
                    topU + x, topV + z);
                AddCell(cells,
                    new Point3(center.X - boxWidth / 2 + x * stepX, center.Y + boxHeight / 2, center.Z - boxDepth / 2 + (z + 1) * stepZ),
                    new Point3(center.X - boxWidth / 2 + (x + 1) * stepX, center.Y + boxHeight / 2, center.Z - boxDepth / 2 + (z + 1) * stepZ),
                    new Point3(center.X - boxWidth / 2 + (x + 1) * stepX, center.Y + boxHeight / 2, center.Z - boxDepth / 2 + z * stepZ),
                    new Point3(center.X - boxWidth / 2 + x * stepX, center.Y + boxHeight / 2, center.Z - boxDepth / 2 + z * stepZ),
                    bottomU + x, bottomV + z);
            }
        }

        private void AddCell(
            List<RenderCell> cells,
            Point3 topLeft,
            Point3 topRight,
            Point3 bottomRight,
            Point3 bottomLeft,
            int textureX,
            int textureY)
        {
            if (_pixels == null || textureX < 0 || textureX >= _skinWidth ||
                textureY < 0 || textureY >= _skinHeight)
                return;

            var index = textureY * _skinWidth * 4 + textureX * 4;
            var alpha = _pixels[index + 3];
            if (alpha < 16)
                return;

            var color = Color.FromArgb(alpha, _pixels[index + 2], _pixels[index + 1], _pixels[index]);
            var a = Project(topLeft);
            var b = Project(topRight);
            var c = Project(bottomRight);
            var d = Project(bottomLeft);
            var depth = (a.Depth + b.Depth + c.Depth + d.Depth) / 4;

            cells.Add(new RenderCell(a.Position, b.Position, c.Position, d.Position, depth, color));
        }

        private ProjectedPoint Project(Point3 point)
        {
            var cosYaw = Math.Cos(_rotation);
            var sinYaw = Math.Sin(_rotation);
            const double pitch = -0.12;
            var cosPitch = Math.Cos(pitch);
            var sinPitch = Math.Sin(pitch);
            var x = point.X * cosYaw + point.Z * sinYaw;
            var z = -point.X * sinYaw + point.Z * cosYaw;
            var y = point.Y * cosPitch - z * sinPitch;
            var depth = point.Y * sinPitch + z * cosPitch;
            var scale = Math.Min(Bounds.Width / 24, Bounds.Height / 36) * 0.88;
            return new ProjectedPoint(
                new Point(Bounds.Width / 2 + x * scale, Bounds.Height / 2 + y * scale),
                depth);
        }

        private IBrush GetBrush(Color color)
        {
            var key = ((uint)color.A << 24) | ((uint)color.R << 16) | ((uint)color.G << 8) | color.B;
            if (!_brushes.TryGetValue(key, out var brush))
            {
                brush = new SolidColorBrush(color);
                _brushes.Add(key, brush);
            }
            return brush;
        }

        private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            {
                _lastPointerPosition = e.GetPosition(this);
                e.Pointer.Capture(this);
            }
        }

        private void OnPointerMoved(object? sender, PointerEventArgs e)
        {
            if (_lastPointerPosition is not Point previous)
                return;

            var current = e.GetPosition(this);
            _rotation += (current.X - previous.X) * 0.012;
            _lastPointerPosition = current;
            InvalidateVisual();
        }

        private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
        {
            _lastPointerPosition = null;
            e.Pointer.Capture(null);
        }

        private readonly record struct Point3(double X, double Y, double Z);
        private readonly record struct ProjectedPoint(Point Position, double Depth);
        private readonly record struct RenderCell(
            Point TopLeft,
            Point TopRight,
            Point BottomRight,
            Point BottomLeft,
            double Depth,
            Color Color);
    }
}
