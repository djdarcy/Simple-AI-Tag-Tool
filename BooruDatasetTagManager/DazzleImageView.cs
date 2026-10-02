using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace BooruDatasetTagManager
{
    /// <summary>
    /// Simple-AI-Tag-Tool: an IrfanView-style image viewer for the Preview tab.
    /// Left-drag draws a selection rectangle that stays; its handles resize it and
    /// dragging inside moves it; clicking inside it zooms to it. Right-drag pans,
    /// the wheel zooms about the pointer, double-click fits the image to the pane.
    /// The control never takes keyboard focus, so Space and the arrows keep stepping
    /// through images; keyboard zoom comes in through the window's hotkeys.
    /// </summary>
    public class DazzleImageView : Control, IMessageFilter
    {
        private const float MinZoom = 0.02f;
        private const float MaxZoom = 64f;
        private const float WheelStep = 1.25f;
        private const int HandleSize = 7;   // screen pixels at 96 dpi
        private const int DragSlop = 3;     // screen pixels before a click becomes a drag

        private Image image;
        private float zoom = 1f;
        private PointF origin;              // image coordinate shown at the control's top-left
        private bool fitMode = true;
        private Rectangle selection;        // image pixels; Empty = none

        private enum Mode { None, Creating, PendingZoom, Moving, Resizing, Panning }
        private Mode mode;
        private Point downScreen;           // where the current gesture started
        private Point lastScreen;
        private Point anchorImage;          // fixed corner while creating
        private Rectangle selectionAtDown;
        private Edge resizeEdges;

        [Flags]
        private enum Edge { None = 0, Left = 1, Top = 2, Right = 4, Bottom = 8 }

        private Cursor zoomCursor;
        private bool filterInstalled;

        /// <summary>Zoom, position or selection changed: the window refreshes its title and info rows.</summary>
        public event EventHandler ViewChanged;

        public DazzleImageView()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            SetStyle(ControlStyles.Selectable, false);
            TabStop = false;
            BackColor = SystemColors.ControlDarkDark;
        }

        /// <summary>Fit never enlarges: an image smaller than the pane shows at 100%, as in IrfanView.</summary>
        public bool FitNeverEnlarges { get; set; } = true;

        public Image Image => image;
        public float Zoom => zoom;
        public Rectangle Selection => selection;
        public bool HasSelection => selection.Width > 0 && selection.Height > 0;

        /// <summary>The whole image's size on screen at the current zoom (IrfanView's "Zoom: W x H").</summary>
        public Size DisplayedSize => image == null ? Size.Empty : new Size((int)Math.Round(image.Width * zoom), (int)Math.Round(image.Height * zoom));

        /// <summary>Width / height of the selection, as IrfanView prints it (1.341).</summary>
        public double SelectionRatio => HasSelection ? (double)selection.Width / selection.Height : 0;

        /// <summary>Show a new image, fitted to the pane, with no selection.</summary>
        public void SetImage(Image img)
        {
            image = img;
            selection = Rectangle.Empty;
            mode = Mode.None;
            ZoomToFit();
        }

        public void ZoomToFit()
        {
            fitMode = true;
            if (image != null && Width > 0 && Height > 0)
            {
                float fit = Math.Min((float)Width / image.Width, (float)Height / image.Height);
                if (FitNeverEnlarges && fit > 1f) fit = 1f;
                zoom = Clamp(fit, MinZoom, MaxZoom);
                CenterOn(new PointF(image.Width / 2f, image.Height / 2f));
            }
            Changed();
        }

        public void ZoomIn() => ZoomAbout(new Point(Width / 2, Height / 2), WheelStep);
        public void ZoomOut() => ZoomAbout(new Point(Width / 2, Height / 2), 1 / WheelStep);

        /// <summary>100%, centred on what is in the middle of the pane now.</summary>
        public void ZoomActual()
        {
            if (image == null) return;
            PointF centre = ScreenToImage(new Point(Width / 2, Height / 2));
            fitMode = false;
            zoom = 1f;
            CenterOn(centre);
            Changed();
        }

        public void ClearSelection()
        {
            if (selection.IsEmpty)
                return;
            selection = Rectangle.Empty;
            Changed();
        }

        /// <summary>Zoom so the selection fills the pane. The selection stays.</summary>
        public void ZoomToSelection()
        {
            if (image == null || selection.Width < 1 || selection.Height < 1)
                return;
            fitMode = false;
            zoom = Clamp(Math.Min((float)Width / selection.Width, (float)Height / selection.Height), MinZoom, MaxZoom);
            CenterOn(new PointF(selection.X + selection.Width / 2f, selection.Y + selection.Height / 2f));
            Changed();
        }

        private void ZoomAbout(Point screen, float factor)
        {
            if (image == null)
                return;
            PointF at = ScreenToImage(screen);
            fitMode = false;
            zoom = Clamp(zoom * factor, MinZoom, MaxZoom);
            origin = new PointF(at.X - screen.X / zoom, at.Y - screen.Y / zoom);
            ClampOrigin();
            Changed();
        }

        private void CenterOn(PointF imagePoint)
        {
            origin = new PointF(imagePoint.X - Width / (2 * zoom), imagePoint.Y - Height / (2 * zoom));
            ClampOrigin();
        }

        /// <summary>Keep the image on screen: centred when smaller than the pane, edge-limited when larger.</summary>
        private void ClampOrigin()
        {
            if (image == null)
                return;
            float viewW = Width / zoom, viewH = Height / zoom;
            origin.X = image.Width <= viewW ? (image.Width - viewW) / 2 : Clamp(origin.X, 0, image.Width - viewW);
            origin.Y = image.Height <= viewH ? (image.Height - viewH) / 2 : Clamp(origin.Y, 0, image.Height - viewH);
        }

        private PointF ScreenToImage(Point p) => new PointF(origin.X + p.X / zoom, origin.Y + p.Y / zoom);

        private RectangleF ImageToScreen(RectangleF r) =>
            new RectangleF((r.X - origin.X) * zoom, (r.Y - origin.Y) * zoom, r.Width * zoom, r.Height * zoom);

        private Point ClampToImage(PointF p) =>
            new Point((int)Math.Round(Clamp(p.X, 0, image.Width)), (int)Math.Round(Clamp(p.Y, 0, image.Height)));

        private static float Clamp(float v, float lo, float hi) => v < lo ? lo : (v > hi ? hi : v);

        private void Changed()
        {
            Invalidate();
            ViewChanged?.Invoke(this, EventArgs.Empty);
        }

        private float DpiScale => DeviceDpi / 96f;

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (fitMode)
                ZoomToFit();
            else
            {
                ClampOrigin();
                Changed();
            }
        }

        // --- the wheel goes to the focused control, not the one under the pointer; redirect it ---

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            if (!filterInstalled) { Application.AddMessageFilter(this); filterInstalled = true; }
        }

        protected override void OnHandleDestroyed(EventArgs e)
        {
            if (filterInstalled) { Application.RemoveMessageFilter(this); filterInstalled = false; }
            base.OnHandleDestroyed(e);
        }

        private const int WM_MOUSEWHEEL = 0x020A;

        bool IMessageFilter.PreFilterMessage(ref Message m)
        {
            if (m.Msg != WM_MOUSEWHEEL || image == null || !Visible || IsDisposed)
                return false;
            // lParam holds the screen position; is the pointer over this control?
            var screen = new Point(unchecked((short)(long)m.LParam), unchecked((short)((long)m.LParam >> 16)));
            if (!RectangleToScreen(ClientRectangle).Contains(screen))
                return false;
            int delta = unchecked((short)((long)m.WParam >> 16));
            ZoomAbout(PointToClient(screen), delta > 0 ? WheelStep : 1 / WheelStep);
            return true; // eaten: the dataset list does not scroll
        }

        // --- painting -------------------------------------------------------------------

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (image == null)
                return;
            var g = e.Graphics;
            // enlarged: hard pixels, like IrfanView; reduced: smooth
            g.InterpolationMode = zoom >= 1f ? InterpolationMode.NearestNeighbor : InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            try
            {
                // only the visible part of the image is handed to GDI+, so a 32x zoom on a big
                // image does not cost a scaled copy of the whole thing
                RectangleF view = new RectangleF(origin.X, origin.Y, Width / zoom, Height / zoom);
                view.Intersect(new RectangleF(0, 0, image.Width, image.Height));
                if (view.Width <= 0 || view.Height <= 0) return;
                g.DrawImage(image, ImageToScreen(view), view, GraphicsUnit.Pixel);
            }
            catch (ArgumentException)
            {
                return; // the image was disposed underneath us (cache off): the next SetImage repaints
            }
            if (selection.IsEmpty)
                return;
            var r = Rectangle.Round(ImageToScreen(selection));
            using (var dark = new Pen(Color.Black))
            using (var light = new Pen(Color.FromArgb(255, 220, 180, 60)) { DashStyle = DashStyle.Dash })
            {
                g.DrawRectangle(dark, r);  // visible on light and dark images alike
                g.DrawRectangle(light, r);
                foreach (var h in HandleRects(r))
                {
                    g.FillRectangle(Brushes.White, h);
                    g.DrawRectangle(dark, h);
                }
            }
        }

        private Rectangle[] HandleRects(Rectangle r)
        {
            int s = (int)Math.Round(HandleSize * DpiScale), hs = s / 2;
            int cx = r.X + r.Width / 2, cy = r.Y + r.Height / 2;
            return new[]
            {
                new Rectangle(r.Left - hs, r.Top - hs, s, s), new Rectangle(cx - hs, r.Top - hs, s, s), new Rectangle(r.Right - hs, r.Top - hs, s, s),
                new Rectangle(r.Left - hs, cy - hs, s, s),                                            new Rectangle(r.Right - hs, cy - hs, s, s),
                new Rectangle(r.Left - hs, r.Bottom - hs, s, s), new Rectangle(cx - hs, r.Bottom - hs, s, s), new Rectangle(r.Right - hs, r.Bottom - hs, s, s),
            };
        }

        private static readonly Edge[] HandleEdges =
        {
            Edge.Left | Edge.Top, Edge.Top, Edge.Right | Edge.Top,
            Edge.Left, Edge.Right,
            Edge.Left | Edge.Bottom, Edge.Bottom, Edge.Right | Edge.Bottom,
        };

        /// <summary>Which handle (edges) is under the point, None when none.</summary>
        private Edge HitHandle(Point p)
        {
            if (selection.IsEmpty)
                return Edge.None;
            var handles = HandleRects(Rectangle.Round(ImageToScreen(selection)));
            for (int i = 0; i < handles.Length; i++)
            {
                var hit = handles[i];
                hit.Inflate(2, 2);
                if (hit.Contains(p))
                    return HandleEdges[i];
            }
            return Edge.None;
        }

        private bool InsideSelection(Point p) =>
            !selection.IsEmpty && Rectangle.Round(ImageToScreen(selection)).Contains(p);

        private static Cursor CursorFor(Edge e)
        {
            switch (e)
            {
                case Edge.Left | Edge.Top:
                case Edge.Right | Edge.Bottom:
                    return Cursors.SizeNWSE;
                case Edge.Right | Edge.Top:
                case Edge.Left | Edge.Bottom:
                    return Cursors.SizeNESW;
                case Edge.Left:
                case Edge.Right:
                    return Cursors.SizeWE;
                default:
                    return Cursors.SizeNS;
            }
        }

        // --- the magnifier cursor: drawn in code, scaled to the monitor, hotspot on the lens ---

        [StructLayout(LayoutKind.Sequential)]
        private struct ICONINFO { public bool fIcon; public int xHotspot; public int yHotspot; public IntPtr hbmMask; public IntPtr hbmColor; }

        [DllImport("user32.dll")] private static extern IntPtr CreateIconIndirect(ref ICONINFO icon);
        [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr h);

        private Cursor ZoomCursor()
        {
            if (zoomCursor != null)
                return zoomCursor;
            float s = DpiScale;
            int size = (int)Math.Round(32 * s);
            using (var bmp = new Bitmap(size, size))
            {
                using (var g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.ScaleTransform(s, s);
                    using (var outline = new Pen(Color.White, 4))
                    using (var pen = new Pen(Color.Black, 2))
                    using (var handle = new Pen(Color.Black, 3))
                    {
                        g.DrawEllipse(outline, 3, 3, 16, 16);
                        g.DrawLine(outline, 17, 17, 27, 27);
                        g.FillEllipse(Brushes.White, 4, 4, 14, 14);
                        g.DrawEllipse(pen, 3, 3, 16, 16);
                        g.DrawLine(handle, 17, 17, 27, 27);
                        g.DrawLine(pen, 7, 11, 15, 11);   // the "+"
                        g.DrawLine(pen, 11, 7, 11, 15);
                    }
                }
                // GetHicon() puts the hotspot in the middle of the bitmap; the lens centre is (11, 11)
                IntPtr hColor = bmp.GetHbitmap(Color.Transparent), hMask = IntPtr.Zero;
                using (var mask = new Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format1bppIndexed))
                    hMask = mask.GetHbitmap();
                var info = new ICONINFO { fIcon = false, xHotspot = (int)Math.Round(11 * s), yHotspot = (int)Math.Round(11 * s), hbmMask = hMask, hbmColor = hColor };
                IntPtr hCursor = CreateIconIndirect(ref info);
                DeleteObject(hColor);
                DeleteObject(hMask);
                zoomCursor = hCursor != IntPtr.Zero ? new Cursor(hCursor) : Cursors.Cross;
            }
            return zoomCursor;
        }

        private void UpdateHoverCursor(Point p)
        {
            if (image == null)
            {
                Cursor = Cursors.Default;
                return;
            }
            var edge = HitHandle(p);
            if (edge != Edge.None)
                Cursor = CursorFor(edge);
            else if (InsideSelection(p))
                Cursor = ZoomCursor();
            else
                Cursor = Cursors.Cross;
        }

        // --- mouse -----------------------------------------------------------------------

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (image == null)
                return;
            downScreen = lastScreen = e.Location;
            if (e.Button == MouseButtons.Right)
            {
                mode = Mode.Panning;
                Cursor = Cursors.Hand;
                Capture = true;
                return;
            }
            if (e.Button != MouseButtons.Left)
                return;
            Capture = true;
            selectionAtDown = selection;
            resizeEdges = HitHandle(e.Location);
            if (resizeEdges != Edge.None)
                mode = Mode.Resizing;
            else if (InsideSelection(e.Location))
                mode = Mode.PendingZoom;
            else
            {
                mode = Mode.Creating;
                anchorImage = ClampToImage(ScreenToImage(e.Location));
                selection = new Rectangle(anchorImage, Size.Empty);
                Changed();
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (image == null)
                return;
            switch (mode)
            {
                case Mode.None:
                    UpdateHoverCursor(e.Location);
                    break;
                case Mode.Panning:
                    fitMode = false;
                    origin = new PointF(origin.X - (e.X - lastScreen.X) / zoom, origin.Y - (e.Y - lastScreen.Y) / zoom);
                    ClampOrigin();
                    Changed();
                    break;
                case Mode.Creating:
                {
                    var p = ClampToImage(ScreenToImage(e.Location));
                    selection = Rectangle.FromLTRB(Math.Min(anchorImage.X, p.X), Math.Min(anchorImage.Y, p.Y),
                                                   Math.Max(anchorImage.X, p.X), Math.Max(anchorImage.Y, p.Y));
                    Changed();
                    break;
                }
                case Mode.PendingZoom:
                    if (Math.Abs(e.X - downScreen.X) > DragSlop * DpiScale || Math.Abs(e.Y - downScreen.Y) > DragSlop * DpiScale)
                    {
                        mode = Mode.Moving;
                        Cursor = Cursors.SizeAll;
                    }
                    break;
                case Mode.Moving:
                {
                    int dx = (int)Math.Round((e.X - downScreen.X) / zoom), dy = (int)Math.Round((e.Y - downScreen.Y) / zoom);
                    int x = Math.Max(0, Math.Min(image.Width - selectionAtDown.Width, selectionAtDown.X + dx));
                    int y = Math.Max(0, Math.Min(image.Height - selectionAtDown.Height, selectionAtDown.Y + dy));
                    selection = new Rectangle(x, y, selectionAtDown.Width, selectionAtDown.Height);
                    Changed();
                    break;
                }
                case Mode.Resizing:
                {
                    var p = ClampToImage(ScreenToImage(e.Location));
                    int l = selectionAtDown.Left, t = selectionAtDown.Top, r = selectionAtDown.Right, b = selectionAtDown.Bottom;
                    if (resizeEdges.HasFlag(Edge.Left)) l = p.X;
                    if (resizeEdges.HasFlag(Edge.Right)) r = p.X;
                    if (resizeEdges.HasFlag(Edge.Top)) t = p.Y;
                    if (resizeEdges.HasFlag(Edge.Bottom)) b = p.Y;
                    selection = Rectangle.FromLTRB(Math.Min(l, r), Math.Min(t, b), Math.Max(l, r), Math.Max(t, b));
                    Changed();
                    break;
                }
            }
            lastScreen = e.Location;
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            Capture = false;
            if (mode == Mode.Creating && (selection.Width < 2 || selection.Height < 2))
                ClearSelection(); // a plain click outside the selection clears it
            else if (mode == Mode.PendingZoom)
                ZoomToSelection(); // click inside the selection: zoom to it
            mode = Mode.None;
            UpdateHoverCursor(e.Location);
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            base.OnMouseDoubleClick(e);
            if (e.Button == MouseButtons.Left && !InsideSelection(e.Location))
                ZoomToFit();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (filterInstalled) { Application.RemoveMessageFilter(this); filterInstalled = false; }
                zoomCursor?.Dispose();
                zoomCursor = null;
            }
            base.Dispose(disposing);
        }
    }
}
