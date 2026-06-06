# Kế hoạch chi tiết nhiệm vụ: resizable-panels

Kế hoạch xây dựng hệ thống cột kéo giãn và thu gọn linh hoạt trong Tab Kiểm thử.

---

## Danh sách công việc (Task Breakdown)

### Task 1: Cấu trúc lại file HTML (`index.html`)
- **Độ ưu tiên:** P1
- **Người thực hiện:** `frontend-specialist`
- **Kỹ năng áp dụng:** `clean-code`
- **Phụ thuộc:** Không
- **INPUT:** Thêm ID và thanh resizer, cấu trúc thanh collapse bar vào [index.html](file:///c:/Users/leuti/Desktop/GitHub/Demo/frontend_vanilla/index.html).
- **OUTPUT:** Cấu trúc DOM đầy đủ sẵn sàng cho CSS và JS tương tác.
- **Xác minh (VERIFY):** Kiểm tra cấu trúc DOM tĩnh bằng cách mở trình duyệt.

---

### Task 2: Cập nhật CSS cho các thanh resizer và trạng thái collapse (`testing.css`)
- **Độ ưu tiên:** P2
- **Người thực hiện:** `frontend-specialist`
- **Kỹ năng áp dụng:** `clean-code`, `frontend-design`
- **Phụ thuộc:** Không
- **INPUT:** Định nghĩa CSS cho resizers, collapsed-bars, collapsed states trong [testing.css](file:///c:/Users/leuti/Desktop/GitHub/Demo/frontend_vanilla/assets/css/components/testing.css).
- **OUTPUT:** Layout hỗ trợ ẩn/mượt và hiển thị chuẩn con trỏ kéo dãn.
- **Xác minh (VERIFY):** Giao diện hiển thị đúng chuẩn.

---

### Task 3: Phát triển logic kéo dãn, đóng/mở và khôi phục layout (`TestingManager.js`)
- **Độ ưu tiên:** P2
- **Người thực hiện:** `frontend-specialist`
- **Kỹ năng áp dụng:** `clean-code`, `frontend-design`
- **Phụ thuộc:** Task 1, Task 2
- **INPUT:** Viết hàm `setupPanelResizers()` trong [TestingManager.js](file:///c:/Users/leuti/Desktop/GitHub/Demo/frontend_vanilla/assets/js/components/TestingManager.js).
- **OUTPUT:** Các cột hoạt động kéo thả và thu gọn mượt mà, lưu trữ vị trí trong localStorage.
- **Xác minh (VERIFY):** Thực hiện kéo thả trực tiếp trên giao diện để kiểm tra tính năng.

---

## Phase X: Xác minh cuối cùng
- [ ] Các thanh resizer kéo dãn mượt mà trong giới hạn min/max.
- [ ] Đóng cột ẩn hoàn toàn, cột kế bên tự động co giãn 100% không gian trống.
- [ ] Mở rộng lại cột khôi phục đúng kích thước trước khi đóng.
- [ ] Trạng thái panel được lưu trữ chính xác qua F5.
- [ ] Không có lỗi JavaScript runtime phát sinh.
