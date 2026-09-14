// Run with Unity Pipeline: command eval_file --file Tools/RenderingRegression.cs
// Uses isolated textures and renderers; does not change the active scene.

var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
var results = new System.Collections.Generic.List<object>();
var passed = true;
foreach (var rotated in new[] { false, true })
foreach (var throttle in new[] { false, true })
{
    // The second size also exercises partial tiles and partial coarse blocks at both edges.
    var width = rotated ? 130 : 128;
    var height = rotated ? 98 : 96;
    var budget = (FractalVisio.Rendering.CpuWorkerBudget)Activator.CreateInstance(typeof(FractalVisio.Rendering.CpuWorkerBudget), flags,
        null, new object[] { 4, 1 }, null);
    if (throttle) typeof(FractalVisio.Rendering.CpuWorkerBudget).GetField("allowed", flags).SetValue(budget, 1);
    var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
    var renderer = new FractalVisio.Rendering.FractalCpuRenderer(new FractalVisio.Rendering.EscapeColorMapper(), budget, false);
    try
    {
        var definition = new FractalVisio.Fractals.MandelbrotDefinition();
        var view = definition.DefaultView;
        if (rotated)
        {
            view.x = -0.743643887037151m;
            view.y = 0.131825904205330m;
            view.scale = 0.0001m;
            view.rotation = 0.37d;
            view.iterations = 512;
        }
        renderer.Request(texture, new FractalVisio.Core.Viewport(width, height), definition,
            default(FractalVisio.Core.FractalParameterSet), view, view.iterations, false);
        var task = (System.Threading.Tasks.Task)typeof(FractalVisio.Rendering.FractalCpuRenderer).GetField("renderTask", flags).GetValue(renderer);
        var completed = task.Wait(1500);
        var mismatches = 0;
        if (completed)
        {
            renderer.Update();
            var values = (float[])typeof(FractalVisio.Rendering.FractalCpuRenderer).GetField("escape", flags).GetValue(renderer);
            var sampler = new FractalVisio.Fractals.MandelbrotSamplerD();
            for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                var nx = ((x + 0.5d) / width - 0.5d) * (width / (double)height);
                var ny = (y + 0.5d) / height - 0.5d;
                var cx = view.x.AsDouble + view.scale.AsDouble * (nx * Math.Cos(view.rotation) - ny * Math.Sin(view.rotation));
                var cy = view.y.AsDouble + view.scale.AsDouble * (nx * Math.Sin(view.rotation) + ny * Math.Cos(view.rotation));
                var expected = sampler.Sample(cx, cy, view.iterations, System.Threading.CancellationToken.None);
                if (Math.Abs(values[y * width + x] - expected) > 0.0001f) mismatches++;
            }
        }
        else
        {
            renderer.Invalidate();
            task.Wait(1500);
        }
        passed &= completed && mismatches == 0 && renderer.PublishedStep == 1;
        results.Add(new { rotated, throttle, completed, mismatches, pixels = width * height });
    }
    finally
    {
        renderer.Dispose();
        UnityEngine.Object.DestroyImmediate(texture);
    }
}
return new { passed, results };
