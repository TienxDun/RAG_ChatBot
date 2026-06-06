# Kế hoạch chi tiết nhiệm vụ: edit-test-cases

Kế hoạch thực hiện tính năng chỉnh sửa và lưu bộ câu hỏi test cases trực tiếp trên giao diện.

---

## Tổng quan & Loại dự án
- **Loại dự án:** WEB + BACKEND (.NET Core API + Vanilla JS)
- **Mục tiêu:** Cho phép người dùng chỉnh sửa danh sách test cases, thêm/xóa câu hỏi và phần mới, lưu trực tiếp xuống file `test_cases.md` trên server từ giao diện web.

---

## Tiêu chí thành công (Success Criteria)
1. Người dùng có thể bật/tắt chế độ Edit Mode.
2. Có thể sửa trực tiếp câu hỏi, thêm câu hỏi, xóa câu hỏi, sửa tên phần, thêm phần mới.
3. Khi bấm "Lưu", file `test_cases.md` trên server được cập nhật chính xác và đồng bộ tức thì lên runtime.
4. Giao diện mượt mà, trực quan, hỗ trợ nút "Hủy thay đổi" để rollback lại trạng thái cũ.

---

## Danh sách công việc (Task Breakdown)

### Task 1: Xây dựng API `POST /api/testcases` trên Backend
- **Độ ưu tiên:** P1
- **Người thực hiện:** `backend-specialist`
- **Kỹ năng áp dụng:** `clean-code`, `api-patterns`
- **Phụ thuộc:** Không
- **INPUT:** Request HTTP POST chứa JSON payload có dạng:
  ```json
  [
    {
      "section": "Tên phần",
      "questions": ["Câu hỏi 1", "Câu hỏi 2"]
    }
  ]
  ```
- **OUTPUT:** File `test_cases.md` và `bin/Debug/net8.0/test_cases.md` được cập nhật định dạng Markdown chuẩn.
- **Xác minh (VERIFY):**
  - Chạy `dotnet build` để kiểm tra biên dịch thành công.
  - Sử dụng PowerShell gửi thử request HTTP POST với dữ liệu test và kiểm tra nội dung file được tạo ra.

---

### Task 2: Thiết lập giao diện điều khiển và các nút chức năng trên Frontend UI
- **Độ ưu tiên:** P2
- **Người thực hiện:** `frontend-specialist`
- **Kỹ năng áp dụng:** `clean-code`, `frontend-design`
- **Phụ thuộc:** Không
- **INPUT:** Chỉnh sửa file `index.html` và `testing.css` để thêm nút Bật chỉnh sửa, Lưu thay đổi, Hủy thay đổi, Thêm phần mới, và định dạng các input field.
- **OUTPUT:** Các phần tử UI hiển thị đúng vị trí theo triết lý thiết kế glassmorphism hiện có.
- **Xác minh (VERIFY):** Kiểm tra hiển thị tĩnh trên giao diện.

---

### Task 3: Hiện thực hóa logic xử lý và lưu trữ trên Frontend
- **Độ ưu tiên:** P2
- **Người thực hiện:** `frontend-specialist`
- **Kỹ năng áp dụng:** `clean-code`, `frontend-design`
- **Phụ thuộc:** Task 1, Task 2
- **INPUT:** Chỉnh sửa `TestingManager.js` để xử lý các sự kiện:
  - Bật/Tắt chế độ edit và sao lưu mảng gốc.
  - Render giao diện chỉnh sửa động.
  - Các thao tác: Sửa câu hỏi, Thêm câu hỏi mới, Xóa câu hỏi, Sửa tiêu đề phần, Thêm phần mới.
  - Nút Hủy khôi phục lại dữ liệu gốc.
  - Nút Lưu gọi API cập nhật server và hiển thị Toast thông báo.
- **OUTPUT:** Hệ thống hoạt động trơn tru từ frontend tới backend.
- **Xác minh (VERIFY):**
  - Trải nghiệm toàn bộ luồng sử dụng trên trình duyệt web.
  - Đảm bảo khi lưu, danh sách tự động được load lại từ server thành công.

---

## Phase X: Xác minh cuối cùng
- [x] Dự án build không lỗi (`dotnet build`).
- [x] Không sử dụng mã màu bị cấm (purple/violet).
- [x] Các tính năng hoạt động đúng nghiệp vụ.
