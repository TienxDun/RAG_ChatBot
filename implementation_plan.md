# Kế hoạch xử lý Template Excel phức tạp (Merged/Hierarchical Headers)

## Bối cảnh & Mục tiêu

Hệ thống hiện tại chỉ xử lý được 2 dạng template đơn giản:
- **Horizontal (ngang):** Một dòng header duy nhất, mỗi cột có tên khác nhau
- **Vertical (dọc):** Đã bị tắt theo yêu cầu trước đó

Tuy nhiên, nhiều biểu mẫu thực tế trong ngành sản xuất/may mặc (VD: "BIÊN BẢN KIỂM HÀNG / END LINE INSPECTION") có cấu trúc phức tạp hơn nhiều. Mục tiêu là nâng cấp hệ thống để tự động nhận diện và xử lý chính xác các template này.

---

## Phân tích 3 thách thức chính

### 1. Tiêu đề phân cấp gộp ô (Merged/Hierarchical Headers)
```
Row 5: | Ngày | Thành Phẩm (merged 4 cols) | Vỏ (merged 4 cols) | Lót thường (merged 4 cols) | Lót lăn (merged 4 cols) |
Row 6: |      | SL.kiểm | SL.lỗi | Tỉ lệ | Ghi chú | SL.kiểm | SL.lỗi | Tỉ lệ | Ghi chú | ...
```
- Dòng 5 chứa header cha (parent) gộp nhiều cột
- Dòng 6 chứa header con (child) với chi tiết

### 2. Trùng lặp tên cột con (Duplicate Sub-headers)
- `SL. kiểm`, `SL. lỗi`, `Tỉ lệ lỗi`, `Ghi chú` lặp lại **4 lần** trên dòng 6
- Hệ thống hiện tại không phân biệt được cột nào thuộc parent nào

### 3. Metadata tĩnh phía trên bảng dữ liệu
- Dòng 3 chứa thông tin phiên làm việc: `Mã Hàng/Style`, `Chuyền/Line`
- Các ô metadata này không phải header, không phải data — cần được xử lý riêng biệt

---

## Proposed Changes

### Component 1: Template Analyzer — Nhận diện cấu trúc template thông minh

#### [NEW] [ExcelTemplateAnalyzer.cs](file:///e:/thuctap/RAG_ChatBot/backend/Services/Excel/ExcelTemplateAnalyzer.cs)

Tạo class mới đảm nhận toàn bộ logic phân tích template, tách khỏi `ExcelReportService`:

```csharp
public class TemplateAnalysisResult
{
    public TemplateType Type { get; set; }  // Simple | Hierarchical
    public int HeaderRowIndex { get; set; }  // Dòng header chính (con)
    public int? ParentHeaderRowIndex { get; set; }  // Dòng header cha (nếu có)
    public int DataStartRowIndex { get; set; }  // Dòng bắt đầu dữ liệu
    public List<FlattenedColumn> Columns { get; set; }  // Danh sách cột đã "làm phẳng"
    public List<MetadataCell> MetadataCells { get; set; }  // Các ô metadata (Mã Hàng, Chuyền...)
    public int StartColumnIndex { get; set; }
}

public class FlattenedColumn
{
    public int ColumnIndex { get; set; }  // Vị trí vật lý trên Excel (1-based)
    public string ParentHeader { get; set; }  // Tên header cha (VD: "Thành Phẩm")
    public string ChildHeader { get; set; }  // Tên header con (VD: "SL. kiểm")
    public string UniqueKey { get; set; }  // Khóa độc nhất = "ThànhPhẩm_SL.kiểm"
    public string FriendlyName { get; set; }  // Tên hiển thị thân thiện cho AI
}

public class MetadataCell
{
    public string Label { get; set; }  // VD: "Mã Hàng/Style"
    public int LabelRow { get; set; }
    public int LabelCol { get; set; }
    public int ValueRow { get; set; }
    public int ValueCol { get; set; }
}
```

**Logic phân tích chính:**

1. **Bước 1: Quét tìm Header Row** (giữ nguyên logic hiện tại — dòng có nhiều cột nhất)
2. **Bước 2: Kiểm tra Merged Cells**
   - Dùng EPPlus `worksheet.MergedCells` để detect dòng phía trên Header Row có merged cells không
   - Nếu có → nhận diện là `Hierarchical` template, dòng trên là Parent Header
3. **Bước 3: Làm phẳng tiêu đề (Flatten Headers)**
   - Duyệt từng merged range ở Parent Header Row
   - Map text của merged cell → tất cả các cột con thuộc range đó
   - Tạo `UniqueKey = ParentHeader + "_" + ChildHeader` cho mỗi cột
4. **Bước 4: Quét Metadata Cells**
   - Quét các dòng từ 1 → ParentHeaderRow - 1
   - Tìm các ô chứa text kết thúc bằng `:` hoặc `/` (pattern "Label: Value")
   - Xác định ô Value tương ứng (ô kế bên phải hoặc ô merged kề cạnh)

---

### Component 2: Cập nhật ExcelReportService — Tích hợp Analyzer

#### [MODIFY] [ExcelReportService.cs](file:///e:/thuctap/RAG_ChatBot/backend/Services/Excel/ExcelReportService.cs)

Thay thế logic phân tích header thủ công (dòng 36-70) bằng call đến `ExcelTemplateAnalyzer`:

```diff
- // 1. Tìm Header Row (Quét 15 dòng đầu...)
- var columns = new List<string>();
- ...
- // (toàn bộ block quét header cũ)
+ // 1. Phân tích cấu trúc Template thông minh
+ var analyzer = new ExcelTemplateAnalyzer();
+ var templateInfo = analyzer.Analyze(worksheet);
```

**Thay đổi trong Prompt gửi AI:**
- Template Simple → Gửi danh sách cột thường (giữ nguyên)
- Template Hierarchical → Gửi danh sách `FlattenedColumn.UniqueKey` kèm mô tả mapping rõ ràng:
  ```
  YÊU CẦU ĐẶC BIỆT CHO TEMPLATE PHÂN CẤP:
  - Cột thứ 2 trên Excel = "Thành Phẩm_SL. kiểm" → Hãy SELECT alias là [ThànhPhẩm_SL.kiểm]
  - Cột thứ 3 = "Thành Phẩm_SL. lỗi" → alias là [ThànhPhẩm_SL.lỗi]
  ...
  - Ngoài ra hãy trả về thông tin metadata: Mã Hàng/Style, Chuyền/Line
  ```

**Thay đổi trong logic ghi dữ liệu:**
- Gọi `ExcelTemplateFiller.FillHierarchicalTemplate()` cho template phân cấp
- Gọi `ExcelTemplateFiller.FillMetadataCells()` để điền metadata

---

### Component 3: Nâng cấp Template Filler — Hỗ trợ ghi dữ liệu phân cấp

#### [MODIFY] [ExcelTemplateFiller.cs](file:///e:/thuctap/RAG_ChatBot/backend/Services/Excel/ExcelTemplateFiller.cs)

Thêm 2 method mới:

**`FillHierarchicalTemplate()`:**
- Xóa dữ liệu cũ từ `DataStartRow` trở xuống (chỉ xóa trong phạm vi các cột header)
- Ghi dữ liệu mới theo **Column Index vật lý** (không theo tên cột)
- Dùng `FlattenedColumn.ColumnIndex` để map chính xác dữ liệu vào đúng cột

**`FillMetadataCells()`:**
- Duyệt danh sách `MetadataCell` từ Analyzer
- Tìm giá trị tương ứng trong response data từ AI
- Ghi vào đúng ô (`ValueRow`, `ValueCol`) giữ nguyên format/font gốc

---

### Component 4: Nâng cấp Styling Helper

#### [MODIFY] [ExcelStylingHelper.cs](file:///e:/thuctap/RAG_ChatBot/backend/Services/Excel/ExcelStylingHelper.cs)

- Thêm method `ApplyHierarchicalHeaderStyle()` để tô màu cả 2 dòng header (parent + child)
- Parent header: Nền đậm hơn (VD: `#B8CCE4`), chữ bold, căn giữa
- Child header: Giữ nguyên style pastel xanh nhẹ hiện tại
- Đảm bảo `SanitizeBorders()` xử lý đúng vùng bao gồm cả parent header row

---

## User Review Required

> [!IMPORTANT]
> **Về Metadata Cells (Mã Hàng, Chuyền...):**
> Khi xử lý template phân cấp, hệ thống cần biết cách AI trả về dữ liệu metadata. Có 2 cách tiếp cận:
> - **Cách A:** AI trả về metadata trong JSON response riêng (VD: `"metadata": {"Mã Hàng": "LSX:752", "Chuyền": "Daisy"}`)
> - **Cách B:** Hệ thống yêu cầu user nhập thông tin metadata bổ sung qua `additionalQuery`
> 
> Tôi đề xuất **Cách A** vì AI có thể tự truy vấn thông tin từ DB. Bạn thấy có phù hợp không?

> [!IMPORTANT]
> **Về cách detect Merged Cells:**
> EPPlus API `worksheet.MergedCells` trả về danh sách các range string (VD: `"B5:E5"`, `"F5:I5"`). Logic sẽ:
> 1. Parse range → xác định start/end column
> 2. Lấy text tại ô top-left của merged range → đó là Parent Header
> 3. Map tất cả cột con nằm trong range → thuộc Parent Header đó
>
> Cách này hoạt động với mọi template có merged cells, không cần hardcode vị trí. Bạn đồng ý?

---

## Open Questions

> [!WARNING]
> **1. Template có 3+ tầng header?**
> Kế hoạch hiện tại hỗ trợ tối đa 2 tầng (Parent + Child). Có trường hợp template nào trong hệ thống của bạn có 3 tầng header trở lên không? Nếu có, tôi cần điều chỉnh thiết kế.

> [!WARNING]
> **2. Xử lý cột không thuộc group nào?**
> Trong template "BIÊN BẢN KIỂM HÀNG", cột đầu tiên "Ngày" không nằm trong bất kỳ merged group nào (nó tồn tại độc lập trên cả 2 dòng header). Kế hoạch hiện tại sẽ set `ParentHeader = ""` và `UniqueKey = "Ngày"` cho các cột này. Bạn thấy hợp lý không?

> [!WARNING]
> **3. Cột tính toán tự động (Tỉ lệ lỗi)?**
> Cột "Tỉ lệ lỗi" trong template thường là công thức `= SL.lỗi / SL.kiểm`. Khi điền dữ liệu mới, hệ thống nên:
> - **A:** Yêu cầu AI tính sẵn tỉ lệ và điền giá trị → Mất công thức gốc
> - **B:** Chỉ điền `SL.kiểm` và `SL.lỗi`, giữ nguyên công thức gốc trong template → Tự động tính
>
> Tôi nghiêng về **Cách B** nhưng cần xác nhận từ bạn.

---

## Verification Plan

### Automated Tests
```bash
# Build project để kiểm tra lỗi compile
dotnet build ./backend

# Chạy thử với template phức tạp (BIÊN BẢN KIỂM HÀNG)
# Verify bằng cách tải file Excel output và kiểm tra:
# 1. Dữ liệu đổ đúng cột (SL.kiểm Thành phẩm ≠ SL.kiểm Vỏ)
# 2. Metadata cells (Mã Hàng, Chuyền) được điền đúng ô
# 3. Merged cells ở header không bị phá vỡ
# 4. Styling (màu, border, font) không bị ảnh hưởng
```

### Manual Verification
- Upload 1 file template đơn giản (không merged) → Xác nhận hoạt động bình thường (regression)
- Upload 1 file template phức tạp (có merged headers) → Xác nhận dữ liệu đổ đúng vị trí
- Kiểm tra file Excel tải về có giữ nguyên merged cells gốc và format template
