import { useEffect } from "react";

const MONTHS = ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];

function todayStr() {
  const d = new Date();
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, "0")}-${String(d.getDate()).padStart(2, "0")}`;
}

// Cross-platform English date picker built from 3 <select> dropdowns.
// Mobile native <input type="date"> ignores lang="en" and uses system locale,
// so we hand-roll the UI to guarantee English regardless of device language.
//
// Defaults to today on mount when no value is provided.
// Value is "YYYY-MM-DD"; onChange always emits a full date.
export default function DatePickerEN({ value, onChange }) {
  // On mount, if no external value yet, sync parent to today so picker + parent agree.
  useEffect(() => {
    if (!value) onChange(todayStr());
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  const effective = value || todayStr();
  const [yStr, mStr, dStr] = effective.split("-");
  const y = Number(yStr), m = Number(mStr), d = Number(dStr);

  const currentYear = new Date().getFullYear();
  const years = [];
  for (let yr = currentYear; yr >= currentYear - 5; yr--) years.push(yr);

  const daysInMonth = new Date(y, m, 0).getDate();
  const days = Array.from({ length: daysInMonth }, (_, i) => i + 1);

  function build(nY, nM, nD) {
    const safeDay = Math.min(nD, new Date(nY, nM, 0).getDate());
    return `${nY}-${String(nM).padStart(2, "0")}-${String(safeDay).padStart(2, "0")}`;
  }

  const selectStyle = {
    padding: "3px 6px",
    border: "1px solid #cbd5e1",
    borderRadius: 4,
    fontSize: 13,
    background: "#fff",
  };

  return (
    <span style={{ display: "inline-flex", gap: 4 }}>
      <select style={selectStyle} value={y} onChange={(e) => onChange(build(Number(e.target.value), m, d))}>
        {years.map((yr) => <option key={yr} value={yr}>{yr}</option>)}
      </select>
      <select style={selectStyle} value={m} onChange={(e) => onChange(build(y, Number(e.target.value), d))}>
        {MONTHS.map((label, i) => <option key={i} value={i + 1}>{label}</option>)}
      </select>
      <select style={selectStyle} value={d} onChange={(e) => onChange(build(y, m, Number(e.target.value)))}>
        {days.map((day) => <option key={day} value={day}>{day}</option>)}
      </select>
    </span>
  );
}
