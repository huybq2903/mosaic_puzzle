using System;
using UnityEngine.UIElements;

namespace Falcon.Shared.EditorUi
{
    public enum KitModalSize
    {
        Sm,
        Md,
        Lg,
        Xl
    }

    public sealed class KitModal : IKitLayer
    {
        public VisualElement Root;
        public VisualElement Box;
        public VisualElement Head;
        public Label Title;
        public Button CloseButton;
        public VisualElement Body;
        public VisualElement Foot;
        public Action OnDismiss;
        public bool Dismissable = true;
        public bool OwnKeys;
        public bool ScrimCloses = true;

        public bool IsOpen { get; private set; }
        public bool Blocking => !OwnKeys;
        public bool Floating => false;
        public bool TakesEscape => !OwnKeys;

        VisualElement IKitLayer.Root => Root;

        public void SetOpen(bool open)
        {
            if (open == IsOpen) return;

            IsOpen = open;
            Kit.SetShown(Root, open);

            if (open)
            {
                Root.BringToFront();
                Kit.Opened(this);
            }
            else
            {
                Kit.Closed(this);
            }
        }

        public void Close()
        {
            SetOpen(false);
        }

        public void Dismiss()
        {
            if (!Dismissable) return;

            SetOpen(false);
            OnDismiss?.Invoke();
        }

        public void SetTitle(string title)
        {
            Kit.SetText(Title, title);
        }

        public void SetWarning(bool warning)
        {
            Box.EnableInClassList("vk-modal-warn", warning);
        }
    }

    public sealed class KitConfirm
    {
        public KitModal Modal;
        public Label Message;
        public TextField Phrase;
        public Label PhraseHint;
        public Button Confirm;
        public Button Cancel;
        public string RequiredPhrase;

        public void Ask(string message = null)
        {
            if (message != null) Kit.SetText(Message, message);

            if (Phrase != null) Phrase.SetValueWithoutNotify("");

            SyncPhrase();
            Modal.SetOpen(true);
        }

        internal void SyncPhrase()
        {
            var ready = string.IsNullOrEmpty(RequiredPhrase) ||
                        string.Equals((Phrase?.value ?? "").Trim(), RequiredPhrase, StringComparison.Ordinal);

            Kit.SetEnabled(Confirm, ready);
        }
    }

    public static partial class Kit
    {
        public static KitModal Modal(string title, KitModalSize size = KitModalSize.Md, Action onDismiss = null,
            VisualElement layer = null)
        {
            var host = layer ?? ModalLayer;
            var modal = new KitModal { Root = LayerRoot(host, true, "vk-scrim"), OnDismiss = onDismiss };

            modal.Box = Col(modal.Root, KitGap.None, "vk-modal " + ModalSizeClass(size));
            modal.Head = Row(modal.Box, KitGap.Sm, "vk-modal-head");
            modal.Title = Text(modal.Head, title, KitText.Heading);
            Spacer(modal.Head);
            modal.CloseButton = Close(modal.Head, modal.Dismiss, "Đóng  [Esc]", KitSize.Md);
            modal.Body = Col(modal.Box, KitGap.Md, "vk-modal-body");
            modal.Foot = Row(modal.Box, KitGap.Sm, "vk-modal-foot");
            Spacer(modal.Foot);

            modal.Root.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.target == modal.Root && modal.ScrimCloses) modal.Dismiss();
            });

            SetShown(modal.Root, false);

            return modal;
        }

        public static KitConfirm Confirm(string title, string message, string confirmLabel, Action onConfirm,
            bool destructive = true, string requiredPhrase = null, VisualElement layer = null)
        {
            var confirm = new KitConfirm
                          {
                              Modal = Modal(title, KitModalSize.Sm, null, layer),
                              RequiredPhrase = requiredPhrase
                          };

            confirm.Modal.SetWarning(destructive);
            confirm.Message = Text(confirm.Modal.Body, message, KitText.Body, true);

            if (!string.IsNullOrEmpty(requiredPhrase))
            {
                confirm.PhraseHint = Text(confirm.Modal.Body, $"Gõ \"{requiredPhrase}\" để xác nhận", KitText.Dim);
                confirm.Phrase = Input(confirm.Modal.Body, "", _ => confirm.SyncPhrase());
            }

            confirm.Cancel = Button(confirm.Modal.Foot, "Huỷ", confirm.Modal.Dismiss, KitTone.Subtle);
            confirm.Confirm = Button(confirm.Modal.Foot, confirmLabel, () =>
            {
                confirm.Modal.SetOpen(false);
                onConfirm?.Invoke();
            }, destructive ? KitTone.Danger : KitTone.Primary);

            return confirm;
        }

        private static string ModalSizeClass(KitModalSize size)
        {
            return size switch
                   {
                       KitModalSize.Sm => "vk-modal-sm",
                       KitModalSize.Lg => "vk-modal-lg",
                       KitModalSize.Xl => "vk-modal-xl",
                       _ => "vk-modal-md"
                   };
        }
    }
}
