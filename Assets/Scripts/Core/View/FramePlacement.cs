using System;
using UnityEngine;

namespace FractalVisio.Core
{
    /// <summary>
    /// Where an already-rendered frame sits relative to the view being shown right now.
    ///
    /// A rendered buffer is not "the current picture": it is a picture of one particular
    /// <see cref="ViewState"/>. Keep that view next to the pixels and any later view can be shown
    /// by sampling the same pixels through an affine map, instead of rewriting them. That is the
    /// difference between resampling the original once per displayed frame (here) and resampling
    /// an already-resampled buffer over and over (what an in-place reprojection does).
    ///
    /// The map is exact for pan, zoom and rotation together, and it is built in decimal for the
    /// centre difference so deep-zoom precision survives the subtraction.
    /// </summary>
    public readonly struct FramePlacement
    {
        private FramePlacement(Vector4 uvRow0, Vector4 uvRow1, float overhang, bool valid)
        {
            UvRow0 = uvRow0;
            UvRow1 = uvRow1;
            Overhang = overhang;
            IsValid = valid;
        }

        /// <summary>
        /// Display uv (0..1 over what the viewer sees) to frame uv (0..1 over the whole buffer):
        /// <c>uvFrame.x = dot(UvRow0.xyz, float3(uvDisplay, 1))</c>, and likewise y from
        /// <see cref="UvRow1"/>. Two vectors rather than a matrix because a matrix uniform's row
        /// or column order depends on the shader compiler's convention and a dot product does not.
        /// </summary>
        public Vector4 UvRow0 { get; }

        public Vector4 UvRow1 { get; }

        /// <summary>
        /// How far the display falls outside the frame, as a fraction of the display's own size -
        /// the same measure the reference app calls "clip". Zero or less means the frame still
        /// covers everything the viewer can see.
        /// </summary>
        public float Overhang { get; }

        public bool IsValid { get; }

        public bool Covers => IsValid && Overhang <= 0f;

        public static FramePlacement Invalid => new(new Vector4(1f, 0f, 0f, 0f), new Vector4(0f, 1f, 0f, 0f), 1f, false);

        /// <summary>
        /// Build the placement of a frame rendered for <paramref name="frameView"/> into a buffer of
        /// aspect <paramref name="frameAspect"/>, seen through <paramref name="currentView"/> on a
        /// display of aspect <paramref name="displayAspect"/>.
        /// </summary>
        public static FramePlacement Resolve(
            in ViewState frameView,
            double frameAspect,
            in ViewState currentView,
            double displayAspect)
        {
            var frameScale = frameView.scale.AsDouble;
            var currentScale = currentView.scale.AsDouble;
            if (!(frameScale > 0d) || !(currentScale > 0d) ||
                double.IsNaN(frameScale) || double.IsNaN(currentScale) ||
                !(frameAspect > 0d) || !(displayAspect > 0d))
            {
                return Invalid;
            }

            // display uv -> normalised display offset -> plane point -> normalised frame offset.
            //   d_frame = b + k * Rot(theta_cur - theta_frame) * n
            //   b       = Rot(-theta_frame) * ((C_cur - C_frame) / scale_frame)
            var k = currentScale / frameScale;
            var delta = currentView.rotation - frameView.rotation;
            var mCos = Math.Cos(delta) * k;
            var mSin = Math.Sin(delta) * k;

            // Subtract in decimal, divide in double. The subtraction is where deep-zoom precision
            // is won or lost; the quotient is a screen-sized offset, so double carries it fine -
            // and dividing a large difference by a 1e-24 scale in decimal would overflow.
            var gx = (double)(currentView.x.AsDecimal - frameView.x.AsDecimal) / frameScale;
            var gy = (double)(currentView.y.AsDecimal - frameView.y.AsDecimal) / frameScale;
            var fCos = Math.Cos(-frameView.rotation);
            var fSin = Math.Sin(-frameView.rotation);
            var bx = gx * fCos - gy * fSin;
            var by = gx * fSin + gy * fCos;

            // n = (displayAspect * (u - 0.5), v - 0.5); frame uv = (d.x / frameAspect + 0.5, d.y + 0.5).
            var invFrameAspect = 1d / frameAspect;
            var a11 = mCos * displayAspect * invFrameAspect;
            var a12 = -mSin * invFrameAspect;
            var a13 = (bx - (mCos * displayAspect - mSin) * 0.5d) * invFrameAspect + 0.5d;
            var a21 = mSin * displayAspect;
            var a22 = mCos;
            var a23 = by - (mSin * displayAspect + mCos) * 0.5d + 0.5d;

            return new FramePlacement(
                new Vector4((float)a11, (float)a12, (float)a13, 0f),
                new Vector4((float)a21, (float)a22, (float)a23, 0f),
                MeasureOverhang(a11, a12, a13, a21, a22, a23),
                true);
        }

        /// <summary>
        /// Overhang of the display's four corners past the frame's 0..1 range, normalised by how
        /// much of the frame the display spans. Normalising is what makes the number comparable
        /// between frames rendered at different scales, which is the whole point of measuring it.
        /// </summary>
        private static float MeasureOverhang(
            double a11, double a12, double a13,
            double a21, double a22, double a23)
        {
            var minU = double.MaxValue;
            var maxU = double.MinValue;
            var minV = double.MaxValue;
            var maxV = double.MinValue;

            for (var corner = 0; corner < 4; corner++)
            {
                var u = (corner & 1) == 0 ? 0d : 1d;
                var v = (corner & 2) == 0 ? 0d : 1d;
                var fu = a11 * u + a12 * v + a13;
                var fv = a21 * u + a22 * v + a23;

                if (fu < minU) minU = fu;
                if (fu > maxU) maxU = fu;
                if (fv < minV) minV = fv;
                if (fv > maxV) maxV = fv;
            }

            var spanU = Math.Max(1e-9d, maxU - minU);
            var spanV = Math.Max(1e-9d, maxV - minV);
            var overhang = Math.Max(
                Math.Max(-minU, maxU - 1d) / spanU,
                Math.Max(-minV, maxV - 1d) / spanV);

            return (float)overhang;
        }
    }
}
