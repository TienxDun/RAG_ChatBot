import openpyxl

wb = openpyxl.load_workbook(r"c:\Users\leuti\Desktop\GitHub\Demo\data\templates\BCEndlineNgay (22)_Empty.xlsx")
sheet = wb.active

print("Dimensions:", sheet.dimensions)
for r in range(1, 15):
    row_vals = []
    for c in range(1, 20):
        cell = sheet.cell(row=r, column=c)
        val = cell.value
        # If merged, find its master cell value
        row_vals.append(f"{val} ({cell.coordinate})" if val is not None else "")
    print(f"Row {r:02d}:", " | ".join(row_vals))
