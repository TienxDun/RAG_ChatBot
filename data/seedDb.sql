USE GarmentDB;
GO

SET NOCOUNT ON;
SET XACT_ABORT ON;
BEGIN TRAN;

INSERT INTO PRODUCTION_LINES (LineID, ManagerName, Department) VALUES
('LINE_01', N'Nguyễn Văn A', N'Xưởng 1'),
('LINE_02', N'Trần Thị B', N'Xưởng 1'),
('LINE_03', N'Lê Văn C', N'Xưởng 2'),
('LINE_04', N'Phạm Thị D', N'Xưởng 2'),
('LINE_05', N'Đỗ Văn E', N'Xưởng 2'),
('LINE_06', N'Bùi Thị F', N'Xưởng 3'),
('LINE_07', N'Vũ Văn G', N'Xưởng 3'),
('LINE_08', N'Ngô Thị H', N'Xưởng 3'),
('LINE_09', N'Lý Văn K', N'Hoàn thiện'),
('LINE_10', N'Trương Thị L', N'Hoàn thiện'),
('LINE_11', N'Đào Văn M', N'Đóng gói'),
('LINE_12', N'Mai Thị N', N'Đóng gói'),
('LINE_13', N'Hoàng Văn P', N'Kho');

INSERT INTO PRODUCTS (ProductID, ProductName, UnitPrice, Description) VALUES
('TSHIRT_WHT', N'Áo thun trắng', 45000, N''),
('PANTS_KHK', N'Quần Kaki', 120000, N''),
('SHIRT_BLU', N'Áo sơ mi xanh', 85000, N''),
('JEAN_BLK', N'Quần Jean đen', 150000, N''),
('DRESS_FLR', N'Đầm hoa', 220000, N''),
('POLO_RED', N'Áo Polo đỏ', 65000, N''),
('JACKET_WND', N'Áo khoác gió', 110000, N''),
('SHORT_NVY', N'Quần đùi navy', 40000, N''),
('SKIRT_BRN', N'Chân váy nâu', 75000, N''),
('HOODIE_GRY', N'Áo Hoodie xám', 135000, N''),
('CAP_BLK', N'Nón kết đen', 25000, N''),
('SOCK_SET', N'Set vớ cotton', 15000, N''),
('JACKET_HVY', N'Áo phao dày', 350000, N'');

INSERT INTO SHIFTS (ShiftID, ShiftName, StartTime, EndTime) VALUES
(1, N'Ca Sáng', '06:00:00', '14:00:00'),
(2, N'Ca Chiều', '14:00:00', '22:00:00'),
(3, N'Ca Đêm', '22:00:00', '06:00:00'),
(4, N'Ca Hành chính', '08:00:00', '17:00:00'),
(5, N'Tăng Ca Sáng', '06:00:00', '08:00:00'),
(6, N'Tăng Ca Tối', '17:00:00', '20:00:00'),
(7, N'Cuối tuần 1', '07:00:00', '15:00:00'),
(8, N'Cuối tuần 2', '15:00:00', '23:00:00'),
(9, N'Ca Lễ Sáng', '06:00:00', '14:00:00'),
(10, N'Ca Lễ Chiều', '14:00:00', '22:00:00'),
(11, N'Ca Dự phòng 1', '09:00:00', '13:00:00'),
(12, N'Ca Dự phòng 2', '13:00:00', '17:00:00');

SET IDENTITY_INSERT EMPLOYEES ON;
INSERT INTO EMPLOYEES (EmployeeID, EmployeeName, Position) VALUES
(1, N'Nguyễn Văn A', N'Tổ trưởng'),
(2, N'Trần Thị B', N'Thợ may'),
(3, N'Lê Văn C', N'KCS'),
(4, N'Phạm Văn D', N'Thợ cắt'),
(5, N'Đỗ Thị E', N'Thợ ủi'),
(6, N'Hoàng Văn F', N'Bảo trì máy'),
(7, N'Vũ Thị G', N'Nhân viên đóng gói'),
(8, N'Ngô Văn H', N'Thủ kho'),
(9, N'Lý Thị K', N'KCS'),
(10, N'Trương Văn L', N'Quản đốc'),
(11, N'Bùi Thị M', N'Thợ may'),
(12, N'Đào Văn N', N'Tổ trưởng'),
(13, N'Lưu Văn Bình', N'Thợ vắt sổ');
SET IDENTITY_INSERT EMPLOYEES OFF;

INSERT INTO DEFECT_CATEGORIES (DefectTypeCode, DefectName, Severity, IsRepairable) VALUES
('ERR_001', N'Đứt chỉ / Tuột chỉ', N'Thấp', 1),
('ERR_002', N'Lệch đường may', N'Trung bình', 1),
('ERR_003', N'Rách vải / Thủng lỗ lớn', N'Cao', 0),
('ERR_004', N'Lem màu in', N'Cao', 0),
('ERR_005', N'Thủng lỗ kim', N'Cao', 0),
('ERR_006', N'Sai kích thước (Thông số)', N'Cao', 0),
('ERR_007', N'Nhăn rúm đường may', N'Trung bình', 1),
('ERR_008', N'Lệch canh sọc / Caro', N'Cao', 0),
('ERR_009', N'Ố vàng / Bẩn dầu máy', N'Trung bình', 1),
('ERR_010', N'Bỏ mũi / Nhảy chỉ', N'Trung bình', 1),
('ERR_011', N'Lệch logo / In sai vị trí', N'Cao', 0),
('ERR_012', N'Đóng nút lỏng / Rớt cúc', N'Thấp', 1),
('ERR_013', N'Dư chỉ thừa', N'Thấp', 1);

SET IDENTITY_INSERT PRODUCTION_PLANS ON;
INSERT INTO PRODUCTION_PLANS (PlanID, ProductID, LineID, TargetQuantity, Deadline) VALUES
(1, 'TSHIRT_WHT', 'LINE_01', 5000, '2026-05-01'),
(2, 'PANTS_KHK', 'LINE_02', 2000, '2026-05-10'),
(3, 'SHIRT_BLU', 'LINE_03', 3000, '2026-05-15'),
(4, 'JEAN_BLK', 'LINE_04', 1500, '2026-05-20'),
(5, 'DRESS_FLR', 'LINE_05', 2500, '2026-05-25'),
(6, 'POLO_RED', 'LINE_06', 4000, '2026-05-10'),
(7, 'JACKET_WND', 'LINE_07', 2000, '2026-06-01'),
(8, 'SHORT_NVY', 'LINE_08', 5000, '2026-05-30'),
(9, 'SKIRT_BRN', 'LINE_09', 3500, '2026-05-22'),
(10, 'HOODIE_GRY', 'LINE_10', 1800, '2026-06-10'),
(11, 'CAP_BLK', 'LINE_11', 10000, '2026-05-05'),
(12, 'SOCK_SET', 'LINE_12', 20000, '2026-05-12');
SET IDENTITY_INSERT PRODUCTION_PLANS OFF;

SET IDENTITY_INSERT WORK_ASSIGNMENTS ON;
INSERT INTO WORK_ASSIGNMENTS (AssignmentID, EmployeeID, LineID, ShiftID, WorkDate) VALUES
(1, 1, 'LINE_01', 1, '2026-04-22'),
(2, 2, 'LINE_02', 1, '2026-04-22'),
(3, 3, 'LINE_01', 2, '2026-04-22'),
(4, 4, 'LINE_03', 4, '2026-04-23'),
(5, 5, 'LINE_04', 4, '2026-04-23'),
(6, 6, 'LINE_05', 1, '2026-04-24'),
(7, 7, 'LINE_06', 2, '2026-04-24'),
(8, 8, 'LINE_07', 3, '2026-04-24'),
(9, 9, 'LINE_08', 1, '2026-04-25'),
(10, 10, 'LINE_09', 2, '2026-04-25'),
(11, 11, 'LINE_10', 4, '2026-04-25'),
(12, 12, 'LINE_11', 1, '2026-04-26');
SET IDENTITY_INSERT WORK_ASSIGNMENTS OFF;

SET IDENTITY_INSERT PRODUCTIVITY ON;
INSERT INTO PRODUCTIVITY (ID, LineID, ProductID, PlanID, Quantity, StartTime, EndTime, ShiftID) VALUES
(1, 'LINE_01', 'TSHIRT_WHT', 1, 800, '2026-04-22T06:00:00', '2026-04-22T14:00:00', 1),
(2, 'LINE_02', 'PANTS_KHK', 2, 450, '2026-04-22T06:00:00', '2026-04-22T14:00:00', 1),
(3, 'LINE_01', 'TSHIRT_WHT', 1, 600, '2026-04-22T14:00:00', '2026-04-22T22:00:00', 2),
(4, 'LINE_03', 'SHIRT_BLU', 3, 500, '2026-04-23T08:00:00', '2026-04-23T17:00:00', 4),
(5, 'LINE_04', 'JEAN_BLK', 4, 250, '2026-04-23T08:00:00', '2026-04-23T17:00:00', 4),
(6, 'LINE_05', 'DRESS_FLR', 5, 400, '2026-04-24T06:00:00', '2026-04-24T14:00:00', 1),
(7, 'LINE_06', 'POLO_RED', 6, 700, '2026-04-24T14:00:00', '2026-04-24T22:00:00', 2),
(8, 'LINE_07', 'JACKET_WND', 7, 300, '2026-04-24T22:00:00', '2026-04-25T06:00:00', 3),
(9, 'LINE_08', 'SHORT_NVY', 8, 850, '2026-04-25T06:00:00', '2026-04-25T14:00:00', 1),
(10, 'LINE_09', 'SKIRT_BRN', 9, 600, '2026-04-25T14:00:00', '2026-04-25T22:00:00', 2),
(11, 'LINE_10', 'HOODIE_GRY', 10, 200, '2026-04-25T08:00:00', '2026-04-25T17:00:00', 4),
(12, 'LINE_11', 'CAP_BLK', 11, 1500, '2026-04-26T06:00:00', '2026-04-26T14:00:00', 1),
(13, 'LINE_12', 'SOCK_SET', 12, 3000, '2026-04-26T14:00:00', '2026-04-26T22:00:00', 2);
SET IDENTITY_INSERT PRODUCTIVITY OFF;

SET IDENTITY_INSERT DEFECTS ON;
INSERT INTO DEFECTS (ID, LineID, ProductID, ProductivityID, DefectTypeCode, ShiftID, DefectCount, RecordedAt) VALUES
(1, 'LINE_01', 'TSHIRT_WHT', 1, 'ERR_001', 1, 15, '2026-04-22T14:05:00'),
(2, 'LINE_02', 'PANTS_KHK', 2, 'ERR_002', 1, 10, '2026-04-22T14:05:00'),
(3, 'LINE_01', 'TSHIRT_WHT', 3, 'ERR_004', 2, 20, '2026-04-22T22:05:00'),
(4, 'LINE_03', 'SHIRT_BLU', 4, 'ERR_007', 4, 12, '2026-04-23T17:05:00'),
(5, 'LINE_04', 'JEAN_BLK', 5, 'ERR_012', 4, 8, '2026-04-23T17:05:00'),
(6, 'LINE_05', 'DRESS_FLR', 6, 'ERR_005', 1, 5, '2026-04-24T14:05:00'),
(7, 'LINE_06', 'POLO_RED', 7, 'ERR_011', 2, 25, '2026-04-24T22:05:00'),
(8, 'LINE_07', 'JACKET_WND', 8, 'ERR_006', 3, 4, '2026-04-25T06:05:00'),
(9, 'LINE_08', 'SHORT_NVY', 9, 'ERR_013', 1, 40, '2026-04-25T14:05:00'),
(10, 'LINE_09', 'SKIRT_BRN', 10, 'ERR_009', 2, 7, '2026-04-25T22:05:00'),
(11, 'LINE_10', 'HOODIE_GRY', 11, 'ERR_008', 4, 2, '2026-04-25T17:05:00'),
(12, 'LINE_11', 'CAP_BLK', 12, 'ERR_010', 1, 15, '2026-04-26T14:05:00'),
(13, 'LINE_12', 'SOCK_SET', 13, 'ERR_013', 2, 10, '2026-04-26T22:05:00');
SET IDENTITY_INSERT DEFECTS OFF;

COMMIT TRAN;