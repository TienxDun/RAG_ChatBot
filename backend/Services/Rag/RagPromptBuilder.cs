namespace Backend.Services.Rag;

/// Tách toàn bộ prompt templates ra file riêng — dễ chỉnh sửa prompt mà không phải đọc qua logic orchestration.
public static class RagPromptBuilder
{
    /// Prompt cho bước Planning: AI đánh giá phạm vi câu hỏi và lập kế hoạch truy vấn SQL.
    public static string BuildPlanningPrompt(string userQuery, string schemaInfo, string globalRules, string currentTimeStr)
    {
        return $@"Bạn là chuyên gia phân tích yêu cầu và lập kế hoạch truy vấn SQL.
                Thời gian hệ thống hiện tại: {currentTimeStr} (Việt Nam, UTC+7).
                Dựa trên CẤU TRÚC DATABASE được cung cấp dưới đây (được trích xuất động từ Qdrant dựa trên ngữ cảnh câu hỏi):
                {schemaInfo}

                {globalRules}

                CÂU HỎI CỦA NGƯỜI DÙNG: ""{userQuery}""

                NHIỆM VỤ BẠN:
                0. QUAN TRỌNG VỀ THỜI GIAN TRUY VẤN: Nếu người dùng hỏi về các khoảng thời gian tương đối/mơ hồ như ""gần đây"", ""gần nhất"", ""mới nhất"", ""hôm nay"", ""tuần này"", ""tháng này"":
                   - Hãy kết hợp với 'Thời gian hệ thống hiện tại' ({currentTimeStr}) để xác định khoảng thời gian cụ thể (ví dụ: ""gần đây/gần nhất"" -> tính ngược từ {currentTimeStr} khoảng 7 ngày hoặc 30 ngày tùy loại dữ liệu).
                   - Nêu rõ mốc thời gian lọc cụ thể này trong phần mô tả bước để bước SQL kế tiếp thực thi đúng.
                1. Kiểm tra xem câu hỏi có liên quan đến dữ liệu trong các bảng trên hay không. Nếu không liên quan đến database, hãy đặt `isOutOfScope: true`.
                2. Nếu câu hỏi liên quan đến database, hãy phân tích xem câu hỏi có bị mơ hồ, thiếu thông tin gom nhóm (GROUP BY) hoặc thống kê cụ thể hay không (ví dụ: 'top lỗi', 'sản lượng cao nhất'):
                   - Hãy tự động đưa ra quyết định hoặc giả định hợp lý nhất dựa trên cấu trúc CSDL thực tế được cung cấp bên trên 
                   (ví dụ: tự động chọn cột phân tích thích hợp như StyleID hoặc LineX từ các bảng liên quan làm đối tượng gom nhóm GROUP BY).
                   - Lập kế hoạch sinh câu truy vấn SQL để thực thi theo giả định mặc định đó ngay lập tức.
                   - Giải trình rõ lý do tự động quyết định và giả định bạn đã chọn trong trường ""reason"".
                   - **TUYỆT ĐỐI CẤM:** Không được sử dụng hoặc tự bịa ra bất kỳ tên bảng hay tên cột nào không xuất hiện trong cấu trúc database được cung cấp phía trên.
                3. Nếu câu hỏi hợp lệ, hãy đặt `isOutOfScope: false` và chia nhỏ câu hỏi thành các bước truy vấn SQL logic.
                   - BẮT BUỘC GỘP THÀNH 1 BƯỚC DUY NHẤT đối với các câu hỏi thống kê, so sánh, xếp hạng (Ví dụ: Top lỗi, Top chuyền, Chênh lệch sản lượng, Xếp hạng lỗi của chuyền...). 
                   TUYỆT ĐỐI CẤM chia nhỏ việc JOIN bảng, GROUP BY gom nhóm, hay dùng DENSE_RANK() xếp hạng thành các bước truy vấn riêng lẻ. Tạo 1 câu SQL duy nhất có thể giải quyết đồng thời các tác vụ này.
                   - CHỈ ĐƯỢC PHÉP CHIA LÀM NHIỀU BƯỚC (tối đa 3 bước) khi và chỉ khi: Bước sau bắt buộc phải sử dụng giá trị dữ liệu động trả về từ bước trước làm tham số điều kiện lọc 
                   (Ví dụ: Bước 1 tìm MaLenh của một mã hàng, Bước 2 dùng MaLenh đó làm tham số lọc để truy vấn sản lượng).
                4. Mỗi bước phải là một nhiệm vụ TRUY VẤN dữ liệu thực tế. TUYỆT ĐỐI KHÔNG tạo bước chỉ để kết hợp (UNION), định dạng hoặc thực hiện các phép tính so sánh/xếp hạng (RANK, CASE WHEN) 
                mà AI có thể tự suy luận từ kết quả bước trước.

                YÊU CẦU ĐỊNH DẠNG (BẮT BUỘC TRẢ VỀ JSON):
                {{
                    ""isOutOfScope"": true/false,
                    ""reason"": ""Giải thích lý do lập kế hoạch hoặc giả định/quyết định ngầm định được chọn khi gặp câu mơ hồ"",
                    ""steps"": [""Mô tả bước 1"", ""Mô tả bước 2""],
                    ""directSql"": ""Câu lệnh SQL Server duy nhất nếu câu hỏi chỉ cần 1 bước truy vấn duy nhất để trả về kết quả, ngược lại để trống """" ""
                }}";
    }

    /// Prompt cho bước Final Generation: AI tổng hợp kết quả SQL thành câu trả lời Markdown.
    public static string BuildFinalPrompt(string userQuery, bool isOutOfScope, string planningReason, string workingContext, string currentTimeStr)
    {
        return $@"Bạn là trợ lý ảo phân tích dữ liệu doanh nghiệp thông minh.
            Thời gian hệ thống: {currentTimeStr}
            Câu hỏi: ""{userQuery}""
            Trạng thái ngoài phạm vi: {(isOutOfScope ? "CÓ" : "KHÔNG")}
            Giả định/Lý do lập kế hoạch ban đầu: ""{planningReason}""
            
            DỮ LIỆU ĐÃ TRUY VẤN ĐƯỢC:
            {workingContext}

            NHIỆM VỤ & NGUYÊN TẮC BẮT BUỘC CHỐNG ẢO GIÁC (HALLUCINATION):
            1. Nếu `isOutOfScope` là CÓ: Hãy từ chối trả lời một cách lịch sự, giải thích rằng bạn chỉ hỗ trợ các dữ liệu liên quan đến hệ thống quản lý và gợi ý người dùng đặt câu hỏi liên quan.
            2. CẤM TỰ BỊA SỐ LIỆU: Mọi con số, mã hàng, tên chuyền, số lượng lỗi, năng suất trong câu trả lời cuối cùng BẮT BUỘC phải lấy trực tiếp từ phần 'DỮ LIỆU ĐÃ TRUY VẤN ĐƯỢC' ở trên. Tuyệt đối không tự bịa ra bất kỳ con số hoặc thông tin giả lập nào không xuất hiện trong kết quả truy vấn SQL thực tế.
            3. Nếu dữ liệu SQL trống hoặc không có dòng nào: Báo cáo rõ ràng cho người dùng rằng không tìm thấy thông tin phù hợp trong hệ thống cho yêu cầu này. TUYỆT ĐỐI KHÔNG tự phỏng đoán số liệu để trả lời.
            4. CẢNH BÁO NÉN DỮ LIỆU: Nếu trong dữ liệu có dòng 'WarningRules: DỮ LIỆU ĐÃ BỊ THU GỌN', bạn phải hiểu rằng danh sách hiển thị chỉ là 5 dòng mẫu. Tuyệt đối không tự đếm số dòng trong danh sách mẫu đó để đưa vào câu trả lời. Hãy sử dụng giá trị tổng số dòng 'TotalRows' hoặc các kết quả tính toán tổng hợp (SUM, COUNT) đã được tính sẵn bởi câu lệnh SQL.
            5. Trình bày câu trả lời chuyên nghiệp bằng Markdown:
               - Sử dụng ### 💠 Tổng quan: Câu trả lời ngắn gọn, trực diện. BẮT BUỘC phải phân tích điều kiện lọc ngày tháng (WHERE) từ các câu lệnh SQL thực tế đã chạy trong phần 'DỮ LIỆU ĐÃ TRUY VẤN ĐƯỢC' ở trên để xác định và ghi rõ khoảng thời gian dữ liệu thực tế (ví dụ: ""Dữ liệu được thống kê trong khoảng thời gian từ ngày 01/01/2025 đến ngày 31/12/2025""). Tuyệt đối KHÔNG sử dụng thời gian hệ thống hiện tại làm khoảng thời gian của dữ liệu nếu dữ liệu đó thuộc về một khoảng thời gian khác trong quá khứ.
                 * ĐẶC BIỆT QUAN TRỌNG: Khi dữ liệu truy vấn được chứa nhiều thông tin chi tiết (ví dụ: danh sách nhiều chuyền sản xuất, nhiều mã hàng, nhiều ngày...), bạn BẮT BUỘC phải tự động tính toán tổng hợp các số liệu toàn cục để người dùng nắm bắt nhanh ngay trong phần này. Thay vào đó, chỉ nhận xét ngắn gọn xu hướng, tỷ trọng % hoặc chỉ ra đối tượng nổi bật nhất/thấp nhất dưới dạng đúc rút thông tin (insight) nhanh. Các phép tính và tỷ lệ phải chính xác 100% dựa trên dữ liệu thực tế.
                 * Nếu câu hỏi ban đầu mơ hồ/thiếu thông tin gom nhóm hoặc thống kê cụ thể, hãy dựa vào phần 'Giả định/Lý do lập kế hoạch ban đầu' để thuyết minh/giải thích rõ ràng cho người dùng biết hệ thống đã tự động quyết định chọn chiều phân tích, bộ lọc hoặc gom nhóm nào để truy xuất dữ liệu.
               - Sử dụng ### 📋 Chi tiết: Dùng bảng Markdown (tiếng Việt) nếu có danh sách.
               - Định dạng số: Phân cách hàng nghìn (ví dụ: 1.234.567). Đối với số tiền, doanh thu, sản lượng, số lượng, tỷ lệ (%) hoặc các số thập phân khác: Chỉ hiển thị phần thập phân khi con số thực sự có phần lẻ (lẻ thực tế). TUYỆT ĐỐI không thêm phần thập phân rỗng (như .000, ,000 hoặc .00) cho các số nguyên hoặc số tròn. Đối với số lẻ thực tế, chỉ làm tròn tối đa 3 chữ số sau dấu phẩy và không ghi các số 0 thừa ở cuối (ví dụ: 25.71428 -> hiển thị 25,714; 27.5 -> hiển thị 27,5%).
               - Quy tắc định dạng ngày tháng: Hiển thị đầy đủ thông tin ngày, tháng, năm, giờ, phút, giây một cách rõ ràng và nhất quán theo định dạng Việt Nam (ví dụ: '13/01/2026 14:30:15' hoặc '13/01/2026' nếu không có giờ phút) trên giao diện và trong bảng kết quả.
                - Quy tắc nhất quán hiển thị tỷ lệ (BẮT BUỘC):
                    * Nếu kết quả SQL đã có cột tỷ lệ như `TiLeLoi`/`TyLeLoi`, bạn PHẢI dùng đúng giá trị gốc trong cột đó để hiển thị. TUYỆT ĐỐI KHÔNG tự nhân 100, không tự chia 100.
                    * Khi đã có `TiLeLoi`/`TyLeLoi`, TUYỆT ĐỐI KHÔNG được tính lại tỷ lệ từ `TongLoi`, `TongDat` hoặc bất kỳ cột nào khác.
                    * Nếu thêm ký hiệu `%`, vẫn phải giữ nguyên đơn vị gốc từ SQL, làm tròn tối đa 3 chữ số sau dấu phẩy và TUYỆT ĐỐI KHÔNG viết thêm các số 0 vô nghĩa ở cuối phần thập phân (ví dụ: SQL trả 0.196335 -> hiển thị 0,196%; SQL trả 19.63 -> hiển thị 19,63% chứ không viết 19,630%; SQL trả 40 -> hiển thị 40% chứ không viết 40,000%).
                    * Số liệu trong phần `### 💠 Tổng quan` và bảng `### 📋 Chi tiết` phải đồng nhất tuyệt đối với `DỮ LIỆU ĐÃ TRUY VẤN ĐƯỢC`.

            YÊU CẦU ĐẦU RA:
            - Hãy trả về TRỰC TIẾP câu trả lời bằng Markdown tiếng Việt theo các quy tắc trên.
            - TUYỆT ĐỐI KHÔNG đính kèm bất kỳ thông tin bổ sung, mã JSON, metadata hay cấu trúc mapping/excelData nào ở cuối câu trả lời văn bản này.";
    }

    /// Prompt cho bước Metadata Generation: AI sinh JSON metadata cho xuất Excel.
    public static string BuildMetadataPrompt(string userQuery, string metadataContext, System.Collections.Generic.List<string>? metadataKeys = null)
    {
        string metadataInstruction = "";
        if (metadataKeys != null && metadataKeys.Count > 0)
        {
            var keysJson = System.Text.Json.JsonSerializer.Serialize(metadataKeys);
            metadataInstruction = $@"
            3. PHÂN TÍCH VÀ ĐIỀN THÔNG TIN CHUNG (METADATA):
            - Danh sách các nhãn metadata cần xác định giá trị: {keysJson}
            - Với mỗi nhãn trong danh sách trên, hãy phân tích kỹ câu hỏi gốc của người dùng (`userQuery`) xem người dùng có thực sự chỉ định/lọc một giá trị cụ thể cho nhãn đó hay không.
              * Ví dụ: Nếu người dùng hỏi ""lỗi của chuyền Cosmos"" thì chuyền được chỉ định cụ thể là ""Cosmos"".
              * Ví dụ: Nếu người dùng chỉ hỏi ""lỗi của các chuyền"" hoặc không nhắc gì đến chuyền, tức là không có chuyền cụ thể nào được chỉ định.
            - Đối với nhãn được chỉ định cụ thể: Hãy tìm giá trị thực tế tương ứng trong 'CẤU TRÚC DỮ LIỆU ĐÃ TRUY VẤN' (từ 2 dòng mẫu hoặc tên cột) để điền vào.
            - Đối với nhãn KHÔNG được chỉ định cụ thể hoặc được hỏi chung chung: BẮT BUỘC đặt giá trị là ""Tất cả"".
            - Điền kết quả phân tích này vào đối tượng ""metadata"" trong JSON trả về (dưới dạng key-value, ví dụ: {{ ""Chuyền/Line:"": ""Cosmos"", ""PO/Cut:"": ""Tất cả"" }}).
            ";
        }

        return $@"Bạn là chuyên gia phân tích dữ liệu doanh nghiệp. Hãy phân tích ngữ cảnh, câu hỏi của người dùng và cấu trúc dữ liệu đã truy vấn để tạo ra siêu dữ liệu (metadata) dưới dạng JSON.

            Câu hỏi gốc của người dùng: ""{userQuery}""
            CẤU TRÚC DỮ LIỆU ĐÃ TRUY VẤN:
            {metadataContext}

            NHIỆM VỤ:
            Tạo ra thông tin xuất file Excel (excelData hoặc columnMapping, và metadata) liên quan trực tiếp đến dữ liệu và câu hỏi.

            QUY TẮC QUAN TRỌNG VỀ DỮ LIỆU EXCEL:
            1. Nếu dữ liệu đã truy vấn được là một danh sách dài hoặc bảng dữ liệu gốc từ database:
            - Đặt `excelData` là mảng rỗng `[]`.
            - Cung cấp `columnMapping` để dịch tên các cột từ tiếng Anh sang tiếng Việt thân thiện dễ hiểu cho người dùng (ví dụ: {{""MaLenh"": ""Mã Lệnh"", ""TenLenh"": ""Tên Lệnh""}}).
            2. Nếu câu hỏi yêu cầu một bảng tổng hợp/tóm tắt số liệu mới (không có sẵn trực tiếp dạng bảng):
            - Tính toán dữ liệu đó dựa vào 2 dòng mẫu và điền vào mảng đối tượng `excelData` (mỗi đối tượng đại diện cho một hàng).
            - Đặt `columnMapping` là đối tượng rỗng `{{}}`.
            {metadataInstruction}

            YÊU CẦU ĐỊNH DẠNG (BẮT BUỘC TRẢ VỀ JSON KHÔNG BỌC TRONG CODEBLOCK):
            {{
                ""excelData"": [],
                ""columnMapping"": {{}},
                ""metadata"": {{}}
            }}";
    }
}
