import { useEffect, useRef } from "react";
import "./WheelPicker.css";

// iOS-style scroll wheel picker (3 rows visible, center highlighted).
// Touch / mouse drag / mouse wheel all supported via native scroll + CSS scroll-snap.
// On scroll end (debounced), reads the centered item and fires onChange.
export default function WheelPicker({ value, min, max, onChange, itemHeight = 36, width = 84 }) {
  const items = [];
  for (let i = min; i <= max; i++) items.push(i);

  const scrollRef = useRef(null);
  const debounceRef = useRef(null);
  const programmaticScrollRef = useRef(false);
  const isFirstMountRef = useRef(true);

  // Sync external value → scroll position (initial mount + when value changes from outside)
  useEffect(() => {
    if (!scrollRef.current) return;
    const target = (value - min) * itemHeight;
    if (Math.abs(scrollRef.current.scrollTop - target) > 2) {
      programmaticScrollRef.current = true;
      if (isFirstMountRef.current) {
        // Initial mount: snap instantly to avoid the smooth-scroll-fires-onScroll race
        scrollRef.current.scrollTop = target;
        isFirstMountRef.current = false;
        setTimeout(() => { programmaticScrollRef.current = false; }, 50);
      } else {
        scrollRef.current.scrollTo({ top: target, behavior: "smooth" });
        setTimeout(() => { programmaticScrollRef.current = false; }, 400);
      }
    }
  }, [value, min, itemHeight]);

  function handleScroll() {
    if (programmaticScrollRef.current) return;
    clearTimeout(debounceRef.current);
    debounceRef.current = setTimeout(() => {
      if (!scrollRef.current) return;
      const top = scrollRef.current.scrollTop;
      const idx = Math.round(top / itemHeight);
      const newValue = Math.max(min, Math.min(max, min + idx));
      if (newValue !== value) onChange(newValue);
    }, 80);
  }

  function jumpTo(n) {
    if (!scrollRef.current) return;
    programmaticScrollRef.current = true;
    scrollRef.current.scrollTo({ top: (n - min) * itemHeight, behavior: "smooth" });
    setTimeout(() => { programmaticScrollRef.current = false; }, 250);
    if (n !== value) onChange(n);
  }

  return (
    <div className="wheel-picker" style={{ width, height: itemHeight * 3 }}>
      <div
        ref={scrollRef}
        className="wheel-scroll"
        onScroll={handleScroll}
      >
        <div className="wheel-spacer" style={{ height: itemHeight }} />
        {items.map((n) => (
          <div
            key={n}
            className={`wheel-item ${n === value ? "active" : ""}`}
            style={{ height: itemHeight, lineHeight: `${itemHeight}px` }}
            onClick={() => jumpTo(n)}
          >
            {n}
          </div>
        ))}
        <div className="wheel-spacer" style={{ height: itemHeight }} />
      </div>
      <div
        className="wheel-highlight"
        style={{ top: itemHeight, height: itemHeight }}
        aria-hidden="true"
      />
    </div>
  );
}
