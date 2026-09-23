using System;

namespace FractalVisio.App
{
    public enum ScreenshotState
    {
        Idle,

        /// <summary>Waiting for the render to finish, so the saved image is the sharp one.</summary>
        WaitingForRender,

        ReadingPixels,
        Encoding,
        AddingToGallery,

        Saved,
        Failed
    }

    /// <summary>Saves the picture on screen as an image. Offered by the screenshot module.</summary>
    public interface IScreenshotService
    {
        ScreenshotState State { get; }

        /// <summary>Where the last image went, or why it did not, for a confirmation message.</summary>
        string LastMessage { get; }

        /// <summary>Current render completion, meaningful while waiting for a sharp frame.</summary>
        float RenderProgress { get; }

        /// <summary>Seconds spent in the current capture or save stage.</summary>
        float PhaseSeconds { get; }

        /// <summary>Raised when <see cref="State"/> changes.</summary>
        event Action<ScreenshotState> StateChanged;

        /// <summary>Save as soon as the current render has finished. Ignored while one is pending.</summary>
        void Request();
    }
}
