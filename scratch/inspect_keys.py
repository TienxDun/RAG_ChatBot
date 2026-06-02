import re
import unicodedata

def remove_diacritics(text):
    if not text:
        return ""
    # Normalize
    nfkd_form = unicodedata.normalize('NFKD', text)
    result = "".join([c for c in nfkd_form if not unicodedata.combining(c)])
    
    # Handle d/D
    result = result.replace('đ', 'd').replace('Đ', 'D')
    # Keep only letters, digits, and underscores
    result = re.sub(r'[^a-zA-Z0-9_]', '', result)
    return result

def generate_unique_key(parent, child):
    p = remove_diacritics(parent or "").strip()
    c = remove_diacritics(child or "").strip()
    
    if not p: return c
    if not c: return p
    if p.lower() == c.lower(): return c
    return f"{p}_{c}"

# Test with our headers
headers = [
    ("Thành Phẩm\n(Finished)", "Ghi chú\n(Notes)"),
    ("Vỏ\n(Shell)", "Ghi chú\n(Notes)"),
    ("Lót thường\n(Lining)", "Ghi chú\n(Notes)"),
    ("Lót lăn\n(Lining)", "Ghi chú\n(Notes)"),
]

for p, c in headers:
    print(f"Parent: {repr(p)} | Child: {repr(c)} -> UniqueKey: {generate_unique_key(p, c)}")
