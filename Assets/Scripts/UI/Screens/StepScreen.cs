using System;
using System.Collections.Generic;
using FractalVisio.Core;

namespace FractalVisio.UI
{
    /// <summary>
    /// One step of a sequence (a hybrid's row), opened from its row in the fractal panel: move it up
    /// or down, and choose its formula from the list, grouped as the gallery groups the fractals of
    /// the same names. Any choice goes back to the fractal panel, where the row now shows it.
    /// </summary>
    public sealed class StepScreen : BlockScreen
    {
        private readonly Action back;
        private int row;

        /// <param name="back">Returns to the fractal panel.</param>
        public StepScreen(Action back)
        {
            this.back = back;
        }

        /// <summary>Aim the sheet at <paramref name="stepRow"/>; the caller then opens it.</summary>
        public void Show(int stepRow)
        {
            row = stepRow;
            NeedsRebuild = true;
        }

        /// <summary>Back from the sheet is back to the panel it came from.</summary>
        public void GoBack()
        {
            Close();
            back?.Invoke();
        }

        protected override int MaximumColumns => 2;

        protected override string BuildTitle() =>
            Strings.Format("sequence.step_title", row + 1);

        protected override void CollectBlocks(List<Block> blocks)
        {
            var session = Services.Session;
            if (!(session.Definition is IStepSequence sequence))
            {
                return;
            }

            var parameters = session.Parameters;
            var rows = StepSequenceEdit.Rows(sequence, parameters);
            row = Math.Clamp(row, 0, rows - 1);

            if (row > 0)
            {
                blocks.Add(new ActionBlock(Strings.Get("sequence.move_up"), () => Move(sequence, -1)));
            }

            if (row < rows - 1)
            {
                blocks.Add(new ActionBlock(Strings.Get("sequence.move_down"), () => Move(sequence, 1)));
            }

            // The formulas by folder, in the order the folders first appear.
            var current = StepSequenceEdit.Formula(sequence, parameters, row);
            var folders = new List<string>();
            var choices = sequence.Choices;
            for (var i = 0; i < choices.Count; i++)
            {
                if (!folders.Contains(choices[i].Folder))
                {
                    folders.Add(choices[i].Folder);
                }
            }

            foreach (var folder in folders)
            {
                var indices = new List<int>();
                var names = new List<string>();
                var selected = -1;
                for (var i = 0; i < choices.Count; i++)
                {
                    if (choices[i].Folder != folder)
                    {
                        continue;
                    }

                    if (i == current)
                    {
                        selected = indices.Count;
                    }

                    indices.Add(i);
                    names.Add(StepSequenceEdit.ChoiceName(Strings, choices[i]));
                }

                var lit = selected;
                blocks.Add(new OptionsBlock(
                    Strings.FolderName(folder), names,
                    index => Choose(sequence, indices[index]),
                    section => section.SetSelected(lit)));
            }
        }

        protected override void OnTick()
        {
            // Another fractal has no such step to edit.
            if (!(Services.Session.Definition is IStepSequence))
            {
                Close();
            }
        }

        private void Choose(IStepSequence sequence, int choice)
        {
            StepSequenceEdit.SetFormula(Services.Session, sequence, row, choice);
            GoBack();
        }

        private void Move(IStepSequence sequence, int direction)
        {
            StepSequenceEdit.MoveRow(Services.Session, sequence, row, direction);
            GoBack();
        }
    }
}
