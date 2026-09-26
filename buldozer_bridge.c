class MaskOverCursorBridge
{
	protected float m_BrushRadius = 8.0;
	protected int m_BrushShape;
	protected bool m_BrushVisible;
	protected bool m_BrushEraser;
	protected float m_PixelStepX = 1.0;
	protected float m_PixelStepZ = 1.0;
	protected bool m_FullMaskRange;
	protected bool m_FullMaskDistanceApplied;
	protected Shape m_BrushOutline;
	protected Shape m_BrushOutlineAccent;
	protected ref array<Shape> m_TerrainPreviewShapes;
	protected int m_TerrainPreviewGeneration = -1;

	void Start()
	{
		if (!m_TerrainPreviewShapes)
			m_TerrainPreviewShapes = new array<Shape>;
		ClearTerrainPreview();
		m_TerrainPreviewGeneration = -1;
		GetGame().GetCallQueue(CALL_CATEGORY_SYSTEM).RemoveByName(this, "UpdateCursor");
		GetGame().GetCallQueue(CALL_CATEGORY_SYSTEM).CallLater(UpdateCursor, 33, true);
	}

	void UpdateCursor()
	{
		ReadTerrainPreview();
		vector rayStart = GetGame().GetCurrentCameraPosition();
		vector rayEnd = rayStart + GetGame().GetPointerDirection() * 30000.0;
		vector hitPosition;
		vector hitNormal;
		int hitComponent;

		if (!DayZPhysics.RaycastRV(rayStart, rayEnd, hitPosition, hitNormal, hitComponent, NULL, NULL, NULL, false, true))
		{
			ClearBrushShapes();
			return;
		}

		FileHandle file = OpenFile("$profile:MaskOver.cursor", FileMode.WRITE);
		if (file != 0)
		{
			FPrintln(file, hitPosition);
			FPrintln(file, GetGame().GetCurrentCameraDirection());
			CloseFile(file);
		}

		ReadBrushState();
		DrawBrush(hitPosition);
	}

	protected void ReadBrushState()
	{
		FileHandle file = OpenFile("$profile:MaskOver.brush", FileMode.READ);
		if (file == 0)
			return;

		string line;
		if (FGets(file, line) > 0)
			m_BrushRadius = Math.Clamp(line.ToFloat(), 1.0, 256.0);
		if (FGets(file, line) > 0)
			m_BrushShape = Math.Clamp(line.ToInt(), 0, 2);
		if (FGets(file, line) > 0)
			m_BrushVisible = line.ToInt() == 1;
		if (FGets(file, line) > 0)
			m_BrushEraser = line.ToInt() == 1;
		if (FGets(file, line) > 0)
			m_PixelStepX = Math.Clamp(line.ToFloat(), 0.01, 100.0);
		if (FGets(file, line) > 0)
			m_PixelStepZ = Math.Clamp(line.ToFloat(), 0.01, 100.0);
		bool fullMaskRange = false;
		if (FGets(file, line) > 0)
			fullMaskRange = line.ToInt() == 1;
		CloseFile(file);

		if (fullMaskRange != m_FullMaskRange)
		{
			m_FullMaskRange = fullMaskRange;
			ApplyFullMaskViewDistance();
		}
	}

	protected void ApplyFullMaskViewDistance()
	{
		if (m_FullMaskRange == m_FullMaskDistanceApplied)
			return;

		if (m_FullMaskRange)
		{
			GetGame().GetWorld().SetViewDistance(300);
			GetGame().GetWorld().SetObjectViewDistance(300);
		}
		else
		{
			GetGame().GetWorld().SetViewDistance(3500);
			GetGame().GetWorld().SetObjectViewDistance(3500);
		}
		m_FullMaskDistanceApplied = m_FullMaskRange;
	}

	protected vector GroundPoint(float x, float z)
	{
		return Vector(x, GetGame().SurfaceY(x, z) + 0.12, z);
	}

	protected void DrawBrush(vector center)
	{
		ClearBrushShapes();
		if (!m_BrushVisible)
			return;

		int primaryColor = COLOR_CYAN;
		int accentColor = COLOR_MAGENTA;
		if (m_BrushEraser)
		{
			primaryColor = COLOR_YELLOW;
			accentColor = COLOR_RED;
		}

		int brushSize = Math.Round(m_BrushRadius);
		float centerX = Math.Round(center[0] / m_PixelStepX) * m_PixelStepX;
		float centerZ = Math.Round(center[2] / m_PixelStepZ) * m_PixelStepZ;
		if ((brushSize & 1) == 0)
		{
			centerX = centerX - (m_PixelStepX * 0.5);
			centerZ = centerZ + (m_PixelStepZ * 0.5);
		}
		vector pixelCenter = Vector(centerX, center[1], centerZ);

		if (m_BrushShape == 1)
		{
			float halfWidth = brushSize * 0.5 * m_PixelStepX;
			float halfDepth = brushSize * 0.5 * m_PixelStepZ;
			m_BrushOutline = DrawSquare(pixelCenter, halfWidth, halfDepth, primaryColor);
			m_BrushOutlineAccent = DrawSquare(pixelCenter, halfWidth + 0.08, halfDepth + 0.08, accentColor);
		}
		else
		{
			bool organic = m_BrushShape == 2 && brushSize > 2;
			float pixelStep = Math.Max(m_PixelStepX, m_PixelStepZ);
			float visibleRadius = brushSize * 0.5 * pixelStep;
			m_BrushOutline = DrawRound(pixelCenter, visibleRadius, organic, primaryColor);
			m_BrushOutlineAccent = DrawRound(pixelCenter, visibleRadius + 0.08, organic, accentColor);
		}
	}

	protected Shape DrawSquare(vector center, float halfWidth, float halfDepth, int color)
	{
		vector points[5];
		points[0] = GroundPoint(center[0] - halfWidth, center[2] - halfDepth);
		points[1] = GroundPoint(center[0] + halfWidth, center[2] - halfDepth);
		points[2] = GroundPoint(center[0] + halfWidth, center[2] + halfDepth);
		points[3] = GroundPoint(center[0] - halfWidth, center[2] + halfDepth);
		points[4] = points[0];
		return Shape.CreateLines(color, ShapeFlags.TRANSP | ShapeFlags.NOOUTLINE | ShapeFlags.NOZBUFFER, points, 5);
	}

	protected Shape DrawRound(vector center, float radius, bool organic, int color)
	{
		vector points[49];
		for (int index = 0; index <= 48; index++)
		{
			float angle = Math.PI2 * index / 48.0;
			float usedRadius = radius;
			if (organic)
				usedRadius = radius * (0.88 + 0.08 * Math.Sin(index * 2.7) + 0.04 * Math.Cos(index * 5.3));
			float x = center[0] + Math.Cos(angle) * usedRadius;
			float z = center[2] + Math.Sin(angle) * usedRadius;
			points[index] = GroundPoint(x, z);
		}
		return Shape.CreateLines(color, ShapeFlags.TRANSP | ShapeFlags.NOOUTLINE | ShapeFlags.NOZBUFFER, points, 49);
	}

	protected void ClearBrushShapes()
	{
		if (m_BrushOutline)
		{
			m_BrushOutline.Destroy();
			m_BrushOutline = null;
		}
		if (m_BrushOutlineAccent)
		{
			m_BrushOutlineAccent.Destroy();
			m_BrushOutlineAccent = null;
		}
	}

	protected void ClearTerrainPreview()
	{
		if (!m_TerrainPreviewShapes)
			return;
		for (int i = 0; i < m_TerrainPreviewShapes.Count(); i++)
		{
			Shape painted = m_TerrainPreviewShapes.Get(i);
			if (painted)
				painted.Destroy();
		}
		m_TerrainPreviewShapes.Clear();
	}

	protected void ReadTerrainPreview()
	{
		FileHandle file = OpenFile("$profile:MaskOver.terrain", FileMode.READ);
		if (file == 0)
			return;
		string line;
		if (FGets(file, line) <= 0)
		{
			CloseFile(file);
			return;
		}
		int generation = line.ToInt();
		if (generation == m_TerrainPreviewGeneration)
		{
			CloseFile(file);
			return;
		}
		if (FGets(file, line) <= 0)
		{
			CloseFile(file);
			return;
		}
		m_TerrainPreviewGeneration = generation;
		ClearTerrainPreview();
		int previewMode = line.ToInt();
		if (previewMode != 1 && previewMode != 2 && previewMode != 3)
		{
			CloseFile(file);
			return;
		}

		while (FGets(file, line) > 0)
		{
			ref array<string> header = new array<string>;
			line.Split(" ", header);
			if (header.Count() >= 6 && header.Get(0) == "L")
			{
				int lineColor = header.Get(1).ToInt();
				vector linePoints[2];
				linePoints[0] = GroundPreviewPoint(header.Get(2).ToFloat(), header.Get(3).ToFloat());
				linePoints[1] = GroundPreviewPoint(header.Get(4).ToFloat(), header.Get(5).ToFloat());
				m_TerrainPreviewShapes.Insert(Shape.CreateLines(lineColor, ShapeFlags.TRANSP | ShapeFlags.NOOUTLINE | ShapeFlags.NOZBUFFER, linePoints, 2));
				continue;
			}
			if (header.Count() < 3 || header.Get(0) != "G")
				continue;
			int color = header.Get(1).ToInt();
			int cellCount = Math.Max(0, header.Get(2).ToInt());
			for (int cellIndex = 0; cellIndex < cellCount; cellIndex++)
			{
				if (FGets(file, line) <= 0)
					break;
				ref array<string> fields = new array<string>;
				line.Split(" ", fields);
				if (fields.Count() < 3)
					continue;
				float x = fields.Get(0).ToFloat();
				float z = fields.Get(1).ToFloat();
				float halfWidth = Math.Clamp(fields.Get(2).ToFloat(), 0.01, 1000.0) * 0.5;
				float halfDepth = halfWidth;
				if (fields.Count() > 3)
					halfDepth = Math.Clamp(fields.Get(3).ToFloat(), 0.01, 1000.0) * 0.5;
				vector p0 = GroundPreviewPoint(x - halfWidth, z - halfDepth);
				vector p1 = GroundPreviewPoint(x + halfWidth, z - halfDepth);
				vector p2 = GroundPreviewPoint(x + halfWidth, z + halfDepth);
				vector p3 = GroundPreviewPoint(x - halfWidth, z + halfDepth);
				vector triangleA[3];
				triangleA[0] = p0;
				triangleA[1] = p1;
				triangleA[2] = p3;
				vector triangleB[3];
				triangleB[0] = p1;
				triangleB[1] = p2;
				triangleB[2] = p3;
				m_TerrainPreviewShapes.Insert(Shape.CreateTris(0x88000000 | color, ShapeFlags.TRANSP | ShapeFlags.DOUBLESIDE | ShapeFlags.NOOUTLINE | ShapeFlags.NOZWRITE, triangleA, 3));
				m_TerrainPreviewShapes.Insert(Shape.CreateTris(0x88000000 | color, ShapeFlags.TRANSP | ShapeFlags.DOUBLESIDE | ShapeFlags.NOOUTLINE | ShapeFlags.NOZWRITE, triangleB, 3));
			}
		}
		CloseFile(file);
	}

	protected vector GroundPreviewPoint(float x, float z)
	{
		return Vector(x, GetGame().SurfaceY(x, z) + 0.07, z);
	}
}

static ref MaskOverCursorBridge g_MaskOverCursorBridge;

// Canonical install target: P:\scripts\buldozer.c
// Add this at the end of BuldozerMain() and press F10 in Buldozer:
// if (!g_MaskOverCursorBridge)
//     g_MaskOverCursorBridge = new MaskOverCursorBridge();
// g_MaskOverCursorBridge.Start();
