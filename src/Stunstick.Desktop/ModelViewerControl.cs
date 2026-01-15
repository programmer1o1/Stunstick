using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Stunstick.App.Viewer;
using System.Numerics;

namespace Stunstick.Desktop;

public sealed class ModelViewerControl : Control
{
	private static readonly IBrush BackgroundBrush = new SolidColorBrush(Color.FromRgb(18, 18, 18));
	private static readonly IBrush PhysicsFillBrush = new SolidColorBrush(Color.FromArgb(200, 255, 160, 0));
	private static readonly Pen PhysicsPen = new(new SolidColorBrush(Color.FromRgb(255, 160, 0)), thickness: 1);
	private static readonly IBrush[] MaterialFillBrushes =
	{
		new SolidColorBrush(Color.FromArgb(200, 230, 230, 230)),
		new SolidColorBrush(Color.FromArgb(200, 255, 99, 132)),
		new SolidColorBrush(Color.FromArgb(200, 54, 162, 235)),
		new SolidColorBrush(Color.FromArgb(200, 255, 206, 86)),
		new SolidColorBrush(Color.FromArgb(200, 75, 192, 192)),
		new SolidColorBrush(Color.FromArgb(200, 153, 102, 255)),
		new SolidColorBrush(Color.FromArgb(200, 255, 159, 64)),
		new SolidColorBrush(Color.FromArgb(200, 199, 199, 199)),
	};
	private static readonly Pen[] MaterialPens =
	{
		new(new SolidColorBrush(Color.FromRgb(230, 230, 230)), thickness: 1),
		new(new SolidColorBrush(Color.FromRgb(255, 99, 132)), thickness: 1),
		new(new SolidColorBrush(Color.FromRgb(54, 162, 235)), thickness: 1),
		new(new SolidColorBrush(Color.FromRgb(255, 206, 86)), thickness: 1),
		new(new SolidColorBrush(Color.FromRgb(75, 192, 192)), thickness: 1),
		new(new SolidColorBrush(Color.FromRgb(153, 102, 255)), thickness: 1),
		new(new SolidColorBrush(Color.FromRgb(255, 159, 64)), thickness: 1),
		new(new SolidColorBrush(Color.FromRgb(199, 199, 199)), thickness: 1),
	};

	private ModelGeometry? geometry;
	private int? materialFilterIndex;

	private Vector3 target;
	private float distance = 10;
	private float yawRadians = -MathF.PI / 4;
	private float pitchRadians = MathF.PI / 10;

	private bool isDragging;
	private Point lastPointerPosition;

	public ModelGeometry? Geometry => geometry;
	public int? MaterialFilterIndex => materialFilterIndex;

	public void SetGeometry(ModelGeometry? value, bool resetCamera = true)
	{
		geometry = value;
		if (resetCamera)
		{
			ResetCamera();
		}

		InvalidateVisual();
	}

	public void SetMaterialFilter(int? materialIndex)
	{
		materialFilterIndex = materialIndex;
		InvalidateVisual();
	}

	public void ResetCamera()
	{
		if (geometry is null)
		{
			target = default;
			distance = 10;
			yawRadians = -MathF.PI / 4;
			pitchRadians = MathF.PI / 10;
			return;
		}

		target = (geometry.Min + geometry.Max) * 0.5f;

		var size = geometry.Max - geometry.Min;
		var radius = MathF.Max(MathF.Max(MathF.Abs(size.X), MathF.Abs(size.Y)), MathF.Abs(size.Z)) * 0.5f;
		if (radius <= 0)
		{
			radius = 1;
		}

		const float fovRadians = MathF.PI / 3; // 60°
		distance = radius / MathF.Tan(fovRadians * 0.5f) * 1.6f;

		yawRadians = -MathF.PI / 4;
		pitchRadians = MathF.PI / 10;

		InvalidateVisual();
	}

	public override void Render(DrawingContext context)
	{
		base.Render(context);

		var size = Bounds.Size;
		if (size.Width <= 0 || size.Height <= 0)
		{
			return;
		}

		context.FillRectangle(BackgroundBrush, new Rect(size));

		var g = geometry;
		if (g is null || g.Triangles.Count == 0)
		{
			return;
		}

		var projected = new List<ProjectedTriangle>(capacity: Math.Min(g.Triangles.Count, 100_000));

		const float fovRadians = MathF.PI / 3; // 60°
		var scale = 0.5f * (float)Math.Min(size.Width, size.Height) / MathF.Tan(fovRadians * 0.5f);

		var cameraPos = target + new Vector3(
			distance * MathF.Cos(pitchRadians) * MathF.Cos(yawRadians),
			distance * MathF.Cos(pitchRadians) * MathF.Sin(yawRadians),
			distance * MathF.Sin(pitchRadians));

		var forward = Vector3.Normalize(target - cameraPos);
		var upWorld = Vector3.UnitZ;
		var right = Vector3.Cross(forward, upWorld);
		if (right.LengthSquared() < 0.000001f)
		{
			upWorld = Vector3.UnitY;
			right = Vector3.Cross(forward, upWorld);
		}
		right = Vector3.Normalize(right);
		var up = Vector3.Normalize(Vector3.Cross(right, forward));

		var cx = size.Width * 0.5;
		var cy = size.Height * 0.5;

		foreach (var tri in g.Triangles)
		{
			if (materialFilterIndex.HasValue && tri.MaterialIndex != materialFilterIndex.Value)
			{
				continue;
			}

			if (!TryProject(tri.A, cameraPos, right, up, forward, scale, cx, cy, out var a2, out var da))
			{
				continue;
			}
			if (!TryProject(tri.B, cameraPos, right, up, forward, scale, cx, cy, out var b2, out var db))
			{
				continue;
			}
			if (!TryProject(tri.C, cameraPos, right, up, forward, scale, cx, cy, out var c2, out var dc))
			{
				continue;
			}

			var depth = (da + db + dc) / 3f;
			projected.Add(new ProjectedTriangle(a2, b2, c2, depth, tri.MaterialIndex));
		}

		if (projected.Count == 0)
		{
			return;
		}

		projected.Sort(static (left, right) => right.Depth.CompareTo(left.Depth));

		for (var i = 0; i < projected.Count; i++)
		{
			var tri = projected[i];
			var fill = tri.MaterialIndex >= 0
				? MaterialFillBrushes[tri.MaterialIndex % MaterialFillBrushes.Length]
				: PhysicsFillBrush;

			var pen = tri.MaterialIndex >= 0
				? MaterialPens[tri.MaterialIndex % MaterialPens.Length]
				: PhysicsPen;

			var geo = new StreamGeometry();
			using (var geoContext = geo.Open())
			{
				geoContext.BeginFigure(tri.A, isFilled: true);
				geoContext.LineTo(tri.B);
				geoContext.LineTo(tri.C);
				geoContext.EndFigure(isClosed: true);
			}

			context.DrawGeometry(fill, pen, geo);
		}
	}

	protected override void OnPointerPressed(PointerPressedEventArgs e)
	{
		base.OnPointerPressed(e);

		var point = e.GetCurrentPoint(this);
		if (point.Properties.IsLeftButtonPressed)
		{
			isDragging = true;
			lastPointerPosition = e.GetPosition(this);
			e.Pointer.Capture(this);
			e.Handled = true;
		}
	}

	protected override void OnPointerMoved(PointerEventArgs e)
	{
		base.OnPointerMoved(e);

		if (!isDragging)
		{
			return;
		}

		var pos = e.GetPosition(this);
		var delta = pos - lastPointerPosition;
		lastPointerPosition = pos;

		const float rotateSpeed = 0.01f;
		yawRadians += (float)delta.X * rotateSpeed;
		pitchRadians -= (float)delta.Y * rotateSpeed;
		pitchRadians = Math.Clamp(pitchRadians, -1.55f, 1.55f);

		InvalidateVisual();
		e.Handled = true;
	}

	protected override void OnPointerReleased(PointerReleasedEventArgs e)
	{
		base.OnPointerReleased(e);

		if (!isDragging)
		{
			return;
		}

		isDragging = false;
		e.Pointer.Capture(null);
		e.Handled = true;
	}

	protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
	{
		base.OnPointerWheelChanged(e);

		if (geometry is null)
		{
			return;
		}

		var wheel = (float)e.Delta.Y;
		var zoomFactor = MathF.Pow(1.1f, -wheel);
		distance *= zoomFactor;
		distance = Math.Clamp(distance, 0.01f, 1_000_000f);

		InvalidateVisual();
		e.Handled = true;
	}

	private static bool TryProject(
		Vector3 world,
		Vector3 cameraPos,
		Vector3 right,
		Vector3 up,
		Vector3 forward,
		float scale,
		double cx,
		double cy,
		out Point screen,
		out float depth)
	{
		var d = world - cameraPos;
		var xCam = Vector3.Dot(d, right);
		var yCam = Vector3.Dot(d, up);
		var zCam = Vector3.Dot(d, forward);
		depth = zCam;

		if (zCam <= 0.01f || float.IsNaN(zCam) || float.IsInfinity(zCam))
		{
			screen = default;
			return false;
		}

		var sx = cx + xCam * scale / zCam;
		var sy = cy - yCam * scale / zCam;
		screen = new Point(sx, sy);
		return true;
	}

	private readonly record struct ProjectedTriangle(Point A, Point B, Point C, float Depth, int MaterialIndex);
}
