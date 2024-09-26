﻿using Cairo;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace canjewelry.src.items.GemChiselMode
{
    public class HorizontalLineGemChiselMode: GemChiselMode
    {
        public override DrawSkillIconDelegate DrawAction(ICoreClientAPI capi) => Drawrotate_svg;

        public void Drawrotate_svg(Context cr, int x, int y, float width, float height, double[] rgba)
        {
            Pattern pattern;
            Matrix matrix = cr.Matrix;

            cr.Save();
            float w = 119;
            float h = 115;
            float scale = Math.Min(width / w, height / h);
            matrix.Translate(x + Math.Max(0, (width - w * scale) / 2), y + Math.Max(0, (height - h * scale) / 2));
            matrix.Scale(scale, scale);
            cr.Matrix = matrix;

            cr.Operator = Operator.Over;
            cr.LineWidth = 15;
            cr.MiterLimit = 10;
            cr.LineCap = LineCap.Butt;
            cr.LineJoin = LineJoin.Miter;
            pattern = new SolidPattern(rgba[0], rgba[1], rgba[2], rgba[3]);
            cr.SetSource(pattern);

            cr.NewPath();
            cr.MoveTo(100.761719, 29.972656);
            cr.CurveTo(116.078125, 46.824219, 111.929688, 74.050781, 98.03125, 89.949219);
            cr.CurveTo(78.730469, 112.148438, 45.628906, 113.027344, 23.527344, 93.726563);
            cr.CurveTo(-13.023438, 56.238281, 17.898438, 7.355469, 61.082031, 7.5);
            cr.Tolerance = 0.1;
            cr.Antialias = Antialias.Default;
            matrix = new Matrix(1, 0, 0, 1, 219.348174, -337.87843);
            pattern.Matrix = matrix;
            cr.StrokePreserve();
            if (pattern != null) pattern.Dispose();

            cr.Operator = Operator.Over;
            pattern = new SolidPattern(rgba[0], rgba[1], rgba[2], rgba[3]);
            cr.SetSource(pattern);

            cr.NewPath();
            cr.MoveTo(81.890625, 11.0625);
            cr.CurveTo(86.824219, 21.769531, 91.550781, 36.472656, 92.332031, 47.808594);
            cr.LineTo(100.761719, 29.972656);
            cr.LineTo(118.585938, 21.652344);
            cr.CurveTo(107.269531, 20.804688, 92.609375, 15.976563, 81.890625, 11.0625);
            cr.ClosePath();
            cr.MoveTo(81.890625, 11.0625);
            cr.Tolerance = 0.1;
            cr.Antialias = Antialias.Default;
            cr.FillRule = FillRule.Winding;
            cr.FillPreserve();
            if (pattern != null) pattern.Dispose();

            cr.Restore();
        }
    }
}
