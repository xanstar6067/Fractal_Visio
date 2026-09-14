using System;
using System.Threading;
using UnityEngine;

namespace FractalVisio.Rendering
{
    /// <summary>
    /// How many threads the fractal may compute on, shared by every CPU renderer, and how many of
    /// them may actually run this moment.
    ///
    /// Two problems, one owner. First, oversubscription: the main renderer used to take every core
    /// but one and the wide background layer took one or two more on top, so during a zoom-out -
    /// exactly when both run - there were more compute threads than cores, and Unity's main and
    /// render threads queued behind them. Unity needs two cores, not one, and this is where they
    /// are kept free. Second, frame pacing under load: even inside the budget, a phone's cores are
    /// not equal and heat up, and a fractal that saturates them for seconds at a time costs the
    /// gesture its frame rate. So while the viewer is moving the picture, <see cref="Regulate"/>
    /// watches the main thread's frame time and parks workers when frames run late. A picture that
    /// moves smoothly and sharpens a little later beats one that sharpens sooner and stutters.
    ///
    /// Workers are parked at tile boundaries, which bounds how late a park takes effect to one
    /// 64x64 tile. Every renderer's first worker is never parked, so no render can stall outright.
    /// </summary>
    public sealed class CpuWorkerBudget
    {
        /// <summary>EMA weight of the newest frame time.</summary>
        private const double Smoothing = 0.25d;

        /// <summary>Park one worker once the smoothed frame time has been this late for a while.</summary>
        private const double OverloadRatio = 1.3d;

        /// <summary>Release one worker once frames have been comfortably on time for a while.</summary>
        private const double CalmRatio = 1.1d;

        private const double OverloadHoldSeconds = 0.1d;
        private const double CalmHoldSeconds = 0.5d;

        /// <summary>A frame longer than this is a hitch (a GC, a resume), not a trend.</summary>
        private const double MaximumSampleSeconds = 0.1d;

        private volatile int allowed;
        private int backgroundBusy;
        private double smoothedFrame;
        private double overloadSeconds;
        private double calmSeconds;

        private CpuWorkerBudget(int total, int backgroundWorkers)
        {
            Total = Math.Max(1, total);
            BackgroundWorkers = Math.Clamp(backgroundWorkers, 1, Math.Max(1, Total / 2));
            Floor = Math.Max(1, (Total + 1) / 2);
            allowed = Total;
        }

        /// <summary>Threads the foreground renderer computes on when nothing is throttled.</summary>
        public int Total { get; }

        /// <summary>Threads the background (wide field) renderer computes on.</summary>
        public int BackgroundWorkers { get; }

        /// <summary>Never throttle below this: pacing may cost the render half its speed, not more.</summary>
        public int Floor { get; }

        /// <summary>Workers currently allowed to run. Equal to <see cref="Total"/> unless throttled.</summary>
        public int Allowed => allowed;

        /// <param name="backgroundWorkers">What the device profile would like the wide layer to have.</param>
        public static CpuWorkerBudget Detect(int backgroundWorkers)
        {
            var cores = Math.Max(1, SystemInfo.processorCount);

            // Unity's main thread and its render thread both have to get a core promptly, or every
            // frame waits on the fractal. A desktop has enough headroom that one is enough.
            var reserve = Application.isMobilePlatform && cores >= 4 ? 2 : 1;
            return new CpuWorkerBudget(cores - reserve, backgroundWorkers);
        }

        /// <summary>
        /// Park order for one renderer's workers. Lower ranks run longer under throttling. The
        /// foreground keeps its first worker at rank 0 and queues the rest behind the background's
        /// workers: during a zoom-out the backdrop is what fills the screen, and while no
        /// background render runs its ranks are free for the foreground anyway.
        /// </summary>
        internal int[] RanksFor(bool background)
        {
            if (background)
            {
                var ranks = new int[BackgroundWorkers];
                for (var i = 0; i < ranks.Length; i++)
                {
                    ranks[i] = i;
                }

                return ranks;
            }

            var foreground = new int[Total];
            for (var i = 1; i < foreground.Length; i++)
            {
                foreground[i] = BackgroundWorkers + i;
            }

            return foreground;
        }

        /// <summary>Whether a worker of this rank may take its next tile. Called from workers.</summary>
        internal bool MayRun(int rank)
        {
            if (rank == 0)
            {
                return true;
            }

            var limit = allowed + (Volatile.Read(ref backgroundBusy) > 0 ? 0 : BackgroundWorkers);
            return rank < limit;
        }

        internal void BeginBackground() => Interlocked.Increment(ref backgroundBusy);

        internal void EndBackground() => Interlocked.Decrement(ref backgroundBusy);

        /// <summary>
        /// Feed one main-thread frame. Throttles only while <paramref name="interacting"/>: at rest
        /// nothing on screen is moving, and the fastest finish is what the viewer wants.
        /// </summary>
        public void Regulate(double frameSeconds, double targetSeconds, bool interacting)
        {
            if (!interacting || !(targetSeconds > 0d))
            {
                allowed = Total;
                smoothedFrame = targetSeconds;
                overloadSeconds = 0d;
                calmSeconds = 0d;
                return;
            }

            var sample = Math.Min(Math.Max(0d, frameSeconds), MaximumSampleSeconds);
            smoothedFrame += (sample - smoothedFrame) * Smoothing;

            if (smoothedFrame > targetSeconds * OverloadRatio)
            {
                calmSeconds = 0d;
                overloadSeconds += sample;
                if (overloadSeconds >= OverloadHoldSeconds && allowed > Floor)
                {
                    allowed--;
                    overloadSeconds = 0d;
                }
            }
            else if (smoothedFrame < targetSeconds * CalmRatio)
            {
                overloadSeconds = 0d;
                calmSeconds += sample;
                if (calmSeconds >= CalmHoldSeconds && allowed < Total)
                {
                    allowed++;
                    calmSeconds = 0d;
                }
            }
            else
            {
                overloadSeconds = 0d;
                calmSeconds = 0d;
            }
        }
    }
}
