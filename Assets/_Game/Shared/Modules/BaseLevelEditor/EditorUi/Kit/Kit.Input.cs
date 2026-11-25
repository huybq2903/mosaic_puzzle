using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Falcon.Shared.EditorUi
{
    public sealed class KitStepper
    {
        public VisualElement Root;
        public Button Down;
        public Button Up;
        public IntegerField Field;
        public Label Unit;

        public int Min;
        public int Max;
        public int Step = 1;
        public bool WheelAlways;
        public Action<int> OnChange;
        public Action<int> OnNudge;

        public int Value => Field.value;

        public bool Editing
        {
            get
            {
                var focused = Field.focusController?.focusedElement as VisualElement;

                return focused != null && Kit.Holds(Field, focused);
            }
        }

        public void SetValue(int value)
        {
            value = Mathf.Clamp(value, Min, Max);

            if (Field.value != value) Field.SetValueWithoutNotify(value);

            Kit.SetEnabled(Down, value > Min);
            Kit.SetEnabled(Up, value < Max);
        }

        public void Sync(int value)
        {
            if (!Editing) SetValue(value);
        }

        public void SetRange(int min, int max)
        {
            Min = min;
            Max = Mathf.Max(min, max);
            SetValue(Field.value);
        }

        public void Nudge(int steps)
        {
            if (OnNudge != null) OnNudge(steps);
            else Commit(Field.value + steps * Step);
        }

        public void Commit(int value)
        {
            Apply(Field.value, value);
        }

        public void Apply(int before, int value)
        {
            SetValue(value);

            if (Field.value != before) OnChange?.Invoke(Field.value);
        }

        /// <summary>Gõ số là áp sau một nhịp ngừng gõ, không cần Enter; ô trống / chưa thành số thì chờ gõ tiếp.</summary>
        public void ApplyWhileTyping(long pauseMs = 300)
        {
            IVisualElementScheduledItem pending = null;
            Field.RegisterCallback<InputEvent>(_ =>
            {
                pending?.Pause();
                pending = Field.schedule.Execute(() =>
                {
                    if (int.TryParse(Field.text?.Trim(), out var v) && v >= Min) Commit(Mathf.Min(v, Max));
                }).StartingIn(pauseMs);
            });
            // rời ô (bấm sang xe khác…): ô tự chốt số như thường, lượt chờ không được áp muộn vào thứ vừa đổi sang
            Field.RegisterCallback<FocusOutEvent>(_ => pending?.Pause());
        }
    }

    public sealed class KitChoice
    {
        public VisualElement Root;
        public Button Down;
        public Button Up;
        public Label Label;

        public void Set(string text, bool canDown = true, bool canUp = true)
        {
            Kit.SetText(Label, text);
            Kit.SetEnabled(Down, canDown);
            Kit.SetEnabled(Up, canUp);
        }
    }

    public sealed class KitSearch
    {
        public VisualElement Root;
        public TextField Field;
        public Button Clear;

        public string Query => Field.value ?? "";

        public void SetQuery(string query)
        {
            Field.SetValueWithoutNotify(query ?? "");
            Kit.SetShown(Clear, !string.IsNullOrEmpty(query));
        }
    }

    public static partial class Kit
    {
        public static VisualElement Field(VisualElement parent, string label, out Label tag, string tip = null)
        {
            var row = Row(parent, KitGap.Sm, "vk-field");

            tag = Text(row, label, KitText.Dim);
            tag.AddToClassList("vk-field-tag");

            if (!string.IsNullOrEmpty(tip)) Tip(tag, tip);

            return El(row, "vk-field-control");
        }

        public static VisualElement Field(VisualElement parent, string label, string tip = null)
        {
            return Field(parent, label, out _, tip);
        }

        public static TextField Input(VisualElement parent, string value, Action<string> onChange,
            string placeholder = null, bool delayed = false)
        {
            var field = new TextField { value = value ?? "", isDelayed = delayed };

            field.AddToClassList("vk-input");

            if (!string.IsNullOrEmpty(placeholder))
            {
                field.textEdition.placeholder = placeholder;
                field.textEdition.hidePlaceholderOnFocus = true;
                ClearPlaceholderLook(field);
            }

            if (onChange != null) field.RegisterValueChangedCallback(evt => onChange(evt.newValue));

            Put(parent, field);

            return field;
        }

        public static KitSearch Search(VisualElement parent, Action<string> onChange, string placeholder = "Tìm…")
        {
            var search = new KitSearch { Root = Row(parent, KitGap.None, "vk-search") };

            Icon(search.Root, KitGlyph.Search).AddToClassList("vk-search-icon");

            search.Field = Input(search.Root, "", null, placeholder);
            search.Clear = IconButton(search.Root, KitGlyph.Close, "Xoá ô tìm", () =>
            {
                search.SetQuery("");
                onChange?.Invoke("");
            }, KitTone.Subtle, KitSize.Sm);

            search.Field.RegisterValueChangedCallback(evt =>
            {
                SetShown(search.Clear, !string.IsNullOrEmpty(evt.newValue));
                onChange?.Invoke(evt.newValue ?? "");
            });

            SetShown(search.Clear, false);

            return search;
        }

        public static KitChoice Choice(VisualElement parent, Action<int> onNudge)
        {
            var choice = new KitChoice { Root = Row(parent, KitGap.None, "vk-stepper vk-choice") };

            choice.Down = IconButton(choice.Root, KitGlyph.Minus, null, () => onNudge?.Invoke(-1), KitTone.Neutral,
                KitSize.Sm);
            choice.Label = Text(choice.Root, "", KitText.Body);
            choice.Label.AddToClassList("vk-choice-text");
            choice.Up = IconButton(choice.Root, KitGlyph.Plus, null, () => onNudge?.Invoke(1), KitTone.Neutral,
                KitSize.Sm);

            return choice;
        }

        public static KitStepper Stepper(VisualElement parent, int value, int min, int max, Action<int> onChange,
            string unit = null, int step = 1, bool buttons = true)
        {
            var stepper = new KitStepper
                          {
                              Root = Row(parent, KitGap.None, buttons ? "vk-stepper" : "vk-stepper vk-bare"),
                              Min = min,
                              Max = Mathf.Max(min, max),
                              Step = Mathf.Max(1, step),
                              OnChange = onChange
                          };

            if (buttons)
                stepper.Down = IconButton(stepper.Root, KitGlyph.Minus, null, () => stepper.Nudge(-1),
                    KitTone.Neutral, KitSize.Sm);

            stepper.Field = new IntegerField { isDelayed = true };
            stepper.Field.AddToClassList("vk-input");
            stepper.Field.AddToClassList("vk-stepper-field");
            stepper.Field.RegisterValueChangedCallback(evt => stepper.Apply(evt.previousValue, evt.newValue));
            Put(stepper.Root, stepper.Field);

            if (buttons)
                stepper.Up = IconButton(stepper.Root, KitGlyph.Plus, null, () => stepper.Nudge(1),
                    KitTone.Neutral, KitSize.Sm);

            if (!string.IsNullOrEmpty(unit)) stepper.Unit = Text(stepper.Root, unit, KitText.Faint);

            stepper.Root.RegisterCallback<WheelEvent>(evt =>
            {
                if (Mathf.Approximately(evt.delta.y, 0f)) return;
                if (!stepper.WheelAlways && !stepper.Editing) return;

                stepper.Nudge(evt.delta.y < 0f ? 1 : -1);
                evt.StopPropagation();
            });

            stepper.SetValue(value);

            return stepper;
        }
    }
}
