# Trả lời của loader — thư `ad_format` (19/10/2026)

> Nguyên văn phần của loader, do owner chuyển lại.

- **Không cần ký cột.** Field tự vào **silver**, và **gold chưa dựng gì** cho họ dính màn.
- **F1** (ngưỡng *"liền sau"*): để bên đọc **tự chọn khi truy vấn silver**.
- **F2** (CTR native trong kho): **chưa cần**.
- **F3** (gộp cụm native): **giữ không gộp** như bên SDK đang nghiêng.

## Nghĩa của nó với phía mình

| Việc | Kết luận |
|---|---|
| Hai cột `prev_full_screen_ad_format` / `_sec` | ✅ **Hết chặn.** Cứ ship, field tự vào silver. Không phải chờ ký như cụm quảng cáo v2 |
| Ngưỡng "liền sau" | ✅ Đúng thiết kế đã chọn: client **không cắt**, gửi `sec` chính xác. Không phải đổi gì |
| CTR native | ✅ Không mở đường riêng. Giữ nguyên ghi chú trong CHANGELOG + spec: **CTR native không có trong kho**, đọc ở dashboard mediation |
| Gộp cụm native | ✅ **Chốt: không gộp.** Native đi per-impression. Ghi vào §4i là quyết định có chủ, không phải chưa làm |

## ⚠ Một chỗ câu trả lời này KHÔNG phủ

*"Không cần ký cột"* nói về **CỘT**. Còn `f_sdk_live_ops_data` là một **EVENT MỚI** — câu **M3** của
thư `Request-Loader-LiveOpsMarkRules.md`. Hai thứ khác hệ quả:

| | Nếu chưa khai báo |
|---|---|
| **Cột** lạ | bị bỏ qua → chỉ thiếu cột, ký sau là có lại từ thời điểm ký |
| **Event** lạ | **drop cả dòng** → dữ liệu những ngày đầu **mất vĩnh viễn**, không backfill được |

Mà bốn module event (Master League, Race, Sky Path, Endless Treasure) **đã gọi API live-ops** rồi —
nên ngày game đầu tiên ship mấy module đó mà event chưa được khai báo là mất trắng mấy ngày đầu.

~~**Vẫn phải hỏi loader đúng một câu**~~ → ✅ **ĐÃ KÝ, owner xác nhận 20/10.** Hết việc chặn. Phần
phân biệt *cột lạ vs event lạ* ở trên vẫn giữ làm luật chung cho những event mới sau này.
