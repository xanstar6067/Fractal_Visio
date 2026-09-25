using System;

namespace FractalVisio.App
{
    /// <summary>
    /// Renders the session's picture again, off screen, at any size: the picture the viewer sees,
    /// cropped centrally to the requested aspect, drawn by the same engines at the requested
    /// resolution rather than stretched from the screen. Offered by <see cref="FrameExportModule"/>.
    /// </summary>
    public interface IFrameExport
    {
        /// <summary>
        /// Start rendering the current picture at <paramref name="width"/> by <paramref name="height"/>,
        /// each pixel the average of <paramref name="supersampling"/> squared samples. The job runs
        /// over the following frames; null when nothing can be rendered.
        /// </summary>
        IFrameExportJob Begin(int width, int height, int supersampling);
    }

    /// <summary>One render started by <see cref="IFrameExport.Begin"/>. Dispose it when done with it.</summary>
    public interface IFrameExportJob : IDisposable
    {
        int Width { get; }
        int Height { get; }

        /// <summary>0 to 1.</summary>
        float Progress { get; }

        /// <summary>True once the job has finished, well or not.</summary>
        bool IsDone { get; }

        /// <summary>RGBA32, bottom row first - ready for an encoder - once done; null if the render failed or was cancelled.</summary>
        byte[] Pixels { get; }

        /// <summary>Stop rendering. The job becomes done with no pixels.</summary>
        void Cancel();
    }
}
