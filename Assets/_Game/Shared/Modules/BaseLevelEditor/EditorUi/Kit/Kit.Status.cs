using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Falcon.Shared.EditorUi
{
    public sealed class KitEmpty
    {
        public VisualElement Root;
        public Label Label;

        public void Set(string text, Severity severity = Severity.Info)
        {
            Kit.SetShown(Root, !string.IsNullOrEmpty(text));

            if (string.IsNullOrEmpty(text)) return;

            Kit.SetText(Label, text);
            Kit.Tint(Label, severity);
        }
    }

    public sealed class KitProgress
    {
        public VisualElement Root;
        public Label Stage;
        public Label Percent;
        public VisualElement Track;
        public VisualElement Fill;
        public Button Cancel;

        private float shownFraction = -1f;

        public void Set(string stage, float fraction = -1f)
        {
            var running = !string.IsNullOrEmpty(stage);

            Kit.SetShown(Root, running);

            if (!running) return;

            Kit.SetText(Stage, stage);

            var known = fraction >= 0f;

            Kit.SetShown(Percent, known);
            Track.EnableInClassList("vk-indeterminate", !known);

            if (!known)
            {
                shownFraction = -1f;
                Fill.style.width = StyleKeyword.Null;
                return;
            }

            if (Mathf.Approximately(shownFraction, fraction)) return;

            shownFraction = fraction;
            fraction = Mathf.Clamp01(fraction);

            Kit.SetText(Percent, $"{Mathf.RoundToInt(fraction * 100f)}%");
            Fill.style.width = Length.Percent(fraction * 100f);
        }

        public void Hide()
        {
            Set(null);
        }
    }

    public sealed class KitStats
    {
        public VisualElement Root;

        private readonly Dictionary<string, Label> values = new();

        public Label Add(string key, string value = "", string tip = null)
        {
            var row = Kit.Row(Root, KitGap.Sm, "vk-stat");
            var name = Kit.Text(row, key, KitText.Dim);

            name.AddToClassList("vk-stat-key");

            if (!string.IsNullOrEmpty(tip)) Kit.Tip(row, tip);

            Kit.Spacer(row);

            var label = Kit.Text(row, value, KitText.Value);

            values[key] = label;

            return label;
        }

        public void Set(string key, string value, Severity severity = Severity.Info)
        {
            if (!values.TryGetValue(key, out var label)) label = Add(key);

            Kit.SetText(label, value);
            Kit.Tint(label, severity);
        }
    }

    public static partial class Kit
    {
        public static KitEmpty Empty(VisualElement parent, string text, Severity severity = Severity.Info)
        {
            var empty = new KitEmpty { Root = Col(parent, KitGap.Xs, "vk-empty") };

            empty.Label = Text(empty.Root, "", KitText.Dim, true);
            empty.Set(text, severity);

            return empty;
        }

        public static KitProgress Progress(VisualElement parent, Action onCancel = null)
        {
            var progress = new KitProgress { Root = Col(parent, KitGap.Xs, "vk-progress") };
            var line = Row(progress.Root, KitGap.Sm);

            progress.Stage = Text(line, "", KitText.Dim);
            progress.Stage.AddToClassList("vk-grow");
            progress.Percent = Text(line, "", KitText.Caption);

            if (onCancel != null) progress.Cancel = Button(line, "Huỷ", onCancel, KitTone.Subtle, KitSize.Sm);

            progress.Track = El(progress.Root, "vk-progress-track");
            progress.Fill = El(progress.Track, "vk-progress-fill");

            progress.Hide();

            return progress;
        }

        public static KitStats Stats(VisualElement parent)
        {
            return new KitStats { Root = Col(parent, KitGap.None, "vk-stats") };
        }
    }
}
