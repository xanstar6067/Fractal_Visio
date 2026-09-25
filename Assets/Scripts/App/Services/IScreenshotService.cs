using System;

namespace FractalVisio.App
{
    public enum ScreenshotState
    {
        Idle,

        /// <summary>
        /// Waiting for the render to finish, so the saved image is the sharp one - or, for an image of
        /// its own size or supersampled, rendering it. <see cref="IScreenshotService.RenderProgress"/> says how far.
        /// </summary>
        WaitingForRender,

        ReadingPixels,
        Encoding,
        AddingToGallery,

        Saved,
        Failed,
        Cancelled
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

        /// <summary>Whether <see cref="Cancel"/> can stop what is running now: the render, not the writing of the file.</summary>
        bool CanCancel { get; }

        /// <summary>Whether the last saved image can be handed to another app - Android, once it is in the gallery.</summary>
        bool CanShare { get; }

        /// <summary>Raised when <see cref="State"/> changes.</summary>
        event Action<ScreenshotState> StateChanged;

        /// <summary>Save as soon as the current render has finished. Ignored while one is pending.</summary>
        void Request();

        /// <summary>Give up on the image being rendered. Nothing is saved.</summary>
        void Cancel();

        /// <summary>Offer the last saved image to other apps through the system's share sheet.</summary>
        void Share();
    }
}
