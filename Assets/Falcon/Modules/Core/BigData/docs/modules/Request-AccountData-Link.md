# AccountData / AccountBinding — báo liên kết tài khoản cho BigData (đợt 3 B14(c))

> Gửi owner `Core/AccountData` (1.2.0) và `AccountBinding` (Facebook / Google / Apple). Người gửi: owner `Core/BigData`. Căn cứ: hợp
> đồng BigData đợt 3 B14(c) (ba bên ký 23/11).

## Vì sao

Chịu liên kết tài khoản sớm là tín hiệu "định chơi lâu"; từ chối cũng là một lựa chọn. Kho chưa có log nào về đăng nhập / liên kết.
BigData **1.8.0** đã có `FalconBigDataController.Account.OnLink(...)` và `Account.SetLinkedProviders(...)`.
**Không gửi id tài khoản bên ngoài** — chỉ loại tài khoản và hành động.

## Việc phải làm

- [ ] **L1. Thành công.** Trong `AccountManager.OnFbLogin` / `OnGoogleLogin` / `OnAppleLogin`, nhánh id đổi (liên kết mới):

  ```csharp
  FalconBigDataController.Account.OnLink(AccountProvider.Facebook, AccountLinkAction.Success, AccountInitiatedBy.User);
  ```

  Id cũ khác rỗng và khác id mới ⇒ `AccountLinkAction.Switch`. Đăng nhập ngầm lúc mở app ⇒ `AccountInitiatedBy.Auto`.
- [ ] **L2. Danh sách đang liên kết.** Sau mỗi lần đổi `FB_id` / `Google_id` / `Apple_id`, và một lần trong `Init`:

  ```csharp
  FalconBigDataController.Account.SetLinkedProviders(/* các provider có id khác rỗng */);
  ```

  Rỗng = chưa liên kết gì (vẫn gọi, với danh sách rỗng).
- [ ] **L3. Huỷ / lỗi** (module đăng nhập, callback `onFailure` của `SignIn`): `AccountLinkAction.Cancel` nếu người chơi huỷ,
  `AccountLinkAction.Fail` nếu lỗi. Hiện callback không tách hai ca — tách được thì tách, không thì `Fail`.
- [ ] **L4. Mời** (nếu module có màn mời): `AccountLinkAction.PromptShown` với `AccountLinkTrigger` tương ứng.

## Không thuộc ticket này

- Xung đột dữ liệu lưu (`save_conflict`, `local_level` / `cloud_level`): `AccountManager.UpdateDataFromServer` **ghi đè thẳng**, không
  có bước hỏi người chơi — chỉ game có màn chọn riêng mới báo được (game tự gọi `OnLink(..., SaveConflict, conflictChoice: ...)`).
