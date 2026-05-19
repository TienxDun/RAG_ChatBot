# Tài liệu Luồng Xử Lý Xuất Excel trong Hệ Thống

Tài liệu này mô tả chi tiết các luồng xử lý xuất dữ liệu ra file Excel trong hệ thống, bao gồm xuất file dựa trên Template mẫu và xuất dữ liệu lưới thông thường.

---

## 1. Luồng xuất dữ liệu theo File Excel mẫu qua Chat (Excel Template Export)

Luồng này kết hợp giữa việc phân tích cấu trúc của file Excel mẫu được tải lên và truy vấn dữ liệu động bằng cơ chế RAG để điền thông tin vào các cột tương ứng.

### Sơ đồ trình tự (Sequence Diagram)

```mermaid
sequenceDiagram
    autonumber
    actor User as Client (Frontend)
    participant CE as ChatEndpoints
    participant ERS as ExcelReportService
    participant RAG as RagOrchestrator
    participant Cache as File Cache (Memory)

    User->>CE: POST /api/chat (Form-Data: message & file.xlsx)
    Note over CE: Phát hiện tệp đính kèm dạng .xlsx
    CE->>ERS: ProcessExcelTemplateAsync(stream, message, onStep)
    
    rect rgb(240, 248, 255)
        Note over ERS: Phân tích 15 dòng đầu tìm Header Row
        ERS-->>CE: onStep("Excel Template Analysis", "Thông tin danh sách cột")
        CE-->>User: Gửi Event SSE (type = step)
        
        Note over ERS: Gộp câu hỏi của User + Chỉ dẫn ánh xạ cột (Mapping instructions)
        ERS->>RAG: ProcessQueryAsync(combinedQuery, isExcelTemplate: true)
        RAG-->>ERS: Trả về kết quả (RawDataTable hoặc RawData JSON)
        
        alt RAG trả về DataTable gốc (RawDataTable)
            Note over ERS: Map dữ liệu trực tiếp vào DataTable mới khớp các cột tiêu đề
        else RAG chỉ trả về chuỗi JSON (RawData)
            Note over ERS: Gọi ConvertJsonToDataTable() để chuyển đổi & phân tích kiểu số/ngày
        end

        Note over ERS: Xóa các hàng dữ liệu mẫu (dummy data) cũ bên dưới dòng Header
        Note over ERS: Đổ dữ liệu mới vào worksheet (LoadFromDataTable)
        Note over ERS: Duyệt định dạng: Căn phải cột số, căn giữa & định dạng "dd/mm/yyyy" cho cột ngày tháng
        Note over ERS: Thêm viền mỏng (Thin Border) & Tự động căn chỉnh độ rộng cột (AutoFit)
        Note over ERS: Trích xuất TOP 20 dòng làm PreviewData
    end
    
    ERS-->>CE: Trả về ExcelReportResult (ExcelBase64, PreviewData, Text)
    
    Note over CE: Giải mã Base64 thành byte[]
    CE->>Cache: Lưu byte[] vào ConcurrentDictionary với Guid ngẫu nhiên
    
    CE-->>User: Gửi Event SSE (type = final, downloadUrl: /api/download/{id}, previewData)
    
    opt Người dùng click Tải File
        User->>CE: GET /api/download/{id}
        CE->>Cache: Lấy byte[] từ cache
        Cache-->>CE: Trả về byte[] của file Excel
        CE-->>User: Phản hồi file .xlsx (vnd.openxmlformats-officedocument.spreadsheetml.sheet)
    end
```

### Chi tiết các bước xử lý nội bộ (Flowchart)

```mermaid
graph TD
    Start([Bắt đầu ProcessExcelTemplateAsync]) --> ScanHeader[Quét 15 dòng đầu và 50 cột đầu tiên để tìm dòng chứa nhiều cột dữ liệu nhất làm dòng Header]
    ScanHeader --> CheckCols{Tìm thấy cột nào?}
    CheckCols -- Không --> ThrowExc[Ném lỗi: Không tìm thấy hàng tiêu đề trong file template]
    CheckCols -- Có --> BuildQuery[Tạo câu query kết hợp giữa câu hỏi của user và hướng dẫn ép kiểu/alias cột cho AI]
    
    BuildQuery --> CallRAG[Gọi RagOrchestrator để thực hiện truy vấn]
    CallRAG --> ParseMode{Kết quả từ RAG?}
    
    ParseMode -- Có DataTable gốc --> MapColumns[Tạo DataTable mới theo chuẩn template<br/>Ánh xạ dữ liệu dựa trên tên cột sử dụng alias]
    ParseMode -- Chỉ có dữ liệu JSON --> ParseJSON[Gọi ConvertJsonToDataTable<br/>1. Lấy mẫu 50 dòng để phân tích xem cột nào chỉ chứa số<br/>2. Phân tích các trường ngày tháng bằng regex hoặc parse thử<br/>3. Đọc toàn bộ JSON thành dòng dữ liệu]
    
    MapColumns --> CleanDummy[Xóa toàn bộ dữ liệu mẫu cũ nằm bên dưới dòng Header trong template]
    ParseJSON --> CleanDummy
    
    CleanDummy --> LoadData[Ghi dữ liệu mới vào sheet bằng phương thức LoadFromDataTable]
    LoadData --> AutoStyle[Duyệt qua từng cột:<br/>- Căn lề phải cho cột số<br/>- Định dạng dd/mm/yyyy và căn giữa cho cột ngày tháng<br/>- Thêm viền mỏng cho bảng]
    
    AutoStyle --> AutoFit[Tự động căn chỉnh độ rộng cột và trích xuất 20 dòng đầu làm dữ liệu xem trước]
    AutoFit --> Return["Chuyển đổi file Excel thành byte[] / Base64 và trả về kết quả"]
    Return --> End([Kết thúc])
```

---

## 2. Luồng xuất dữ liệu dạng lưới thông thường (Generic Data Grid Export)

Áp dụng khi client có sẵn một mảng dữ liệu JSON (ví dụ kết quả hiển thị trên bảng lưới UI) và muốn tải trực tiếp về dưới dạng file Excel được định dạng sẵn.

### Sơ đồ trình tự (Sequence Diagram)

```mermaid
sequenceDiagram
    autonumber
    actor User as Client (Frontend)
    participant CE as ChatEndpoints
    participant ERS as ExcelReportService

    User->>CE: POST /api/chat/export-excel (JSON Body: danh sách dữ liệu)
    CE->>ERS: ExportGenericExcel(data)
    
    rect rgb(245, 245, 245)
        Note over ERS: Khởi tạo Workbook & Worksheet "Data Export" mới (EPPlus)
        Note over ERS: Lấy danh sách Headers từ các Key của bản ghi đầu tiên
        Note over ERS: Ghi Hàng 1 (Header): In đậm, tô nền xanh (#9DBAD9), chữ trắng, căn giữa, viền mỏng
        
        loop Duyệt qua từng dòng & từng cột dữ liệu
            Note over ERS: Trích xuất giá trị từ JsonElement (Number, Bool, String)
            alt Giá trị là số (double, float, int, long, decimal)
                Note over ERS: Định dạng phân tách hàng nghìn "#,##0" & Căn lề phải
            else Giá trị là ngày tháng (DateTime/DateTimeOffset hoặc tên cột chứa Ngay/Date/Time)
                Note over ERS: Chuyển đổi định dạng "dd/mm/yyyy" & Căn lề giữa
            end
            Note over ERS: Thêm viền mỏng (Thin Border) cho ô dữ liệu
        end
        Note over ERS: Tự động căn chỉnh độ rộng các cột (AutoFitColumns)
        Note over ERS: Chuyển đổi workbook thành mảng Byte
    end
    
    ERS-->>CE: Trả về byte[] của file Excel
    CE-->>User: Trả về file tải xuống với tên mặc định: data_export_yyyyMMddHHmmss.xlsx
```

---

## 3. Các đặc điểm kỹ thuật quan trọng

* **EPPlus License**: Sử dụng giấy phép phi thương mại cho EPPlus thông qua cấu hình `ExcelPackage.License.SetNonCommercialPersonal("My Project")`.
* **Phân tích thông minh (Smart Data Parsing)**:
  * Khi xử lý file mẫu, hệ thống quét 15 hàng đầu tiên để tìm dòng chứa nhiều ô có dữ liệu nhất, dòng đó sẽ được chọn làm Header Row. Dữ liệu mẫu (dummy data) ban đầu nằm dưới Header Row này sẽ được dọn sạch hoàn toàn trước khi điền dữ liệu thực tế.
  * Khi chuyển đổi dữ liệu từ dạng JSON (`ConvertJsonToDataTable`), service lấy trước 50 bản ghi mẫu đầu tiên để tự động dò kiểu dữ liệu (`IsNumeric`) của từng cột, từ đó tự động căn phải đối với cột số, hoặc căn giữa và đặt định dạng `dd/mm/yyyy` cho cột ngày tháng.
* **Tải xuống qua Cache trung gian**: Dữ liệu file Excel tạo từ hội thoại chat được lưu trữ tạm thời trong `ConcurrentDictionary` của endpoint để người dùng có thể tải xuống thông qua định danh duy nhất (Guid) mà không cần gửi lại toàn bộ file qua luồng SSE.
