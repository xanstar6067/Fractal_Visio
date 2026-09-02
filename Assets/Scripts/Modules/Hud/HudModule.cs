using System;
using System.Globalization;
using UnityEngine;
using UnityEngine.UI;
using FractalVisio.App;
using FractalVisio.Core;

namespace FractalVisio.Modules
{
    /// <summary>
    /// The debug readout: view coordinates on top, render engine and progress underneath. First
    /// module, and the shape every later one follows - it reads the session and the render status
    /// through the context, and owns nothing but its own UI objects.
    /// </summary>
    public sealed class HudModule : IAppModule
    {
        private const float UpdateInterval = 0.1f;

        private readonly int fontSize;

        private Text scaleValueText;
        private Text computeBackendText;
        private AppServices context;
        private float nextUpdateTime;

        public HudModule(Text scaleValueText, Text computeBackendText, int fontSize)
        {
            this.scaleValueText = scaleValueText;
            this.computeBackendText = computeBackendText;
            this.fontSize = Mathf.Max(8, fontSize);
        }

        public string Id => "hud";

        public void Initialize(AppServices services)
        {
            context = services;

            if (scaleValueText == null)
            {
                scaleValueText = CreateText("Scale", ScalePosition);
            }

            if (computeBackendText == null)
            {
                computeBackendText = CreateText("Backend", BackendPosition);
            }

            Configure(scaleValueText, ScalePosition);
            Configure(computeBackendText, BackendPosition);
            nextUpdateTime = 0f;
        }

        public void Tick()
        {
            if (context == null || Time.unscaledTime < nextUpdateTime)
            {
                return;
            }

            nextUpdateTime = Time.unscaledTime + UpdateInterval;

            var view = context.Session.View;
            var status = context.Render.Status;

            if (scaleValueText != null)
            {
                var scale = view.scale.AsDouble;
                var reference = ViewState.Default.scale.AsDouble;
                var zoom = scale > 0d ? reference / scale : 0d;
                var rotationDegrees = view.rotation * (180d / Math.PI);
                rotationDegrees -= Math.Floor(rotationDegrees / 360d) * 360d;

                // Full-precision centre so a view can be reproduced on the PC via SetView().
                scaleValueText.text = string.Concat(
                    "scale  ", scale.ToString("0.000000e+00", CultureInfo.InvariantCulture), "\n",
                    "zoom   x", zoom.ToString("0.###e+00", CultureInfo.InvariantCulture), "\n",
                    "rot    ", rotationDegrees.ToString("0.0", CultureInfo.InvariantCulture), " deg\n",
                    "X  ", view.x.AsDecimal.ToString("G29", CultureInfo.InvariantCulture), "\n",
                    "Y  ", view.y.AsDecimal.ToString("G29", CultureInfo.InvariantCulture));
            }

            if (computeBackendText == null)
            {
                return;
            }

            string engineLine;
            string detailLine;
            if (status.Backend == RenderBackend.GpuFloat)
            {
                engineLine = "GPU fp32" + (status.Interacting ? "  interactive" : string.Empty);
                detailLine = "iter " + status.Iterations;
            }
            else
            {
                var precision = status.ExtendedPrecision ? "double-double" : "fp64";
                engineLine = "CPU Parallel  " + precision + (status.Interacting ? "  interactive" : string.Empty);
                detailLine = status.IsBusy
                    ? string.Concat(
                        "iter ", status.Iterations.ToString(CultureInfo.InvariantCulture),
                        "  pass ", status.Pass.ToString(CultureInfo.InvariantCulture),
                        "/", status.PassCount.ToString(CultureInfo.InvariantCulture),
                        "  ", Mathf.RoundToInt(status.Progress * 100f).ToString(CultureInfo.InvariantCulture), "%")
                    : "iter " + status.Iterations + "  done";
            }

            computeBackendText.text = engineLine + "\n" + detailLine;
        }

        public void Shutdown()
        {
            context = null;
        }

        private Vector2 ScalePosition => new(24f, -24f);

        private Vector2 BackendPosition => new(24f, -24f - 5.6f * fontSize);

        private Text CreateText(string objectName, Vector2 anchoredPosition)
        {
            var child = new GameObject(objectName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            child.transform.SetParent(context.UiRoot, false);
            var text = child.GetComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            Configure(text, anchoredPosition);
            return text;
        }

        private void Configure(Text text, Vector2 anchoredPosition)
        {
            if (text == null)
            {
                return;
            }

            // The scene's Text objects point at the old built-in Arial (removed in
            // Unity 6), so they render nothing. Force a font that ships at runtime.
            if (text.font == null || text.font.name != "LegacyRuntime")
            {
                var runtimeFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                if (runtimeFont != null)
                {
                    text.font = runtimeFont;
                }
            }

            var rect = text.rectTransform;
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = new Vector2(3600f, 8f * fontSize);
            text.alignment = TextAnchor.UpperLeft;
            text.fontSize = fontSize;
            text.fontStyle = FontStyle.Bold;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.color = Color.white;
            text.raycastTarget = false;

            // High-contrast edge so the readout stays legible over any fractal colour.
            var outline = text.GetComponent<Outline>();
            if (outline == null)
            {
                outline = text.gameObject.AddComponent<Outline>();
            }

            outline.effectColor = Color.black;
            outline.effectDistance = new Vector2(4f, -4f);
        }
    }
}
