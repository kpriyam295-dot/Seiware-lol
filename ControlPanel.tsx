import { useState } from 'react';
import { useTheme } from '../ThemeContext';

const PRESETS = [
  { name: 'Red', hex: '#dc2626' },
  { name: 'Cyan', hex: '#06b6d4' },
  { name: 'Green', hex: '#22c55e' },
  { name: 'Purple', hex: '#a855f7' },
  { name: 'Orange', hex: '#f97316' },
  { name: 'Pink', hex: '#ec4899' },
  { name: 'Blue', hex: '#3b82f6' },
  { name: 'Yellow', hex: '#eab308' },
];

export default function ControlPanel() {
  const { theme, setHex, setTitle, setBlurEnabled, setFunFact } = useTheme();
  const [open, setOpen] = useState(false);
  const [localTitle, setLocalTitle] = useState(theme.title);
  const [localFact, setLocalFact] = useState(theme.funFact);

  const c = `${theme.r}, ${theme.g}, ${theme.b}`;

  const handleTitleChange = (val: string) => {
    setLocalTitle(val);
    setTitle(val || 'DREAMLAND');
  };

  const handleFactChange = (val: string) => {
    setLocalFact(val);
    setFunFact(val);
  };

  return (
    <>
      {/* Toggle button */}
      <button
        data-hover
        onClick={() => setOpen(!open)}
        className="fixed top-5 right-16 z-[60] w-8 h-8 flex items-center justify-center border transition-all duration-300 select-none"
        style={{
          borderColor: `rgba(${c}, 0.3)`,
          color: `rgba(${c}, 0.6)`,
          backgroundColor: open ? `rgba(${c}, 0.1)` : 'transparent',
        }}
      >
        <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round">
          <circle cx="12" cy="12" r="3" />
          <path d="M19.4 15a1.65 1.65 0 0 0 .33 1.82l.06.06a2 2 0 0 1-2.83 2.83l-.06-.06a1.65 1.65 0 0 0-1.82-.33 1.65 1.65 0 0 0-1 1.51V21a2 2 0 0 1-4 0v-.09A1.65 1.65 0 0 0 9 19.4a1.65 1.65 0 0 0-1.82.33l-.06.06a2 2 0 0 1-2.83-2.83l.06-.06A1.65 1.65 0 0 0 4.68 15a1.65 1.65 0 0 0-1.51-1H3a2 2 0 0 1 0-4h.09A1.65 1.65 0 0 0 4.6 9a1.65 1.65 0 0 0-.33-1.82l-.06-.06a2 2 0 0 1 2.83-2.83l.06.06A1.65 1.65 0 0 0 9 4.68a1.65 1.65 0 0 0 1-1.51V3a2 2 0 0 1 4 0v.09a1.65 1.65 0 0 0 1 1.51 1.65 1.65 0 0 0 1.82-.33l.06-.06a2 2 0 0 1 2.83 2.83l-.06.06A1.65 1.65 0 0 0 19.4 9a1.65 1.65 0 0 0 1.51 1H21a2 2 0 0 1 0 4h-.09a1.65 1.65 0 0 0-1.51 1z" />
        </svg>
      </button>

      {/* Panel */}
      <div
        className="fixed top-16 right-4 z-[55] w-72 border backdrop-blur-md bg-black/70 transition-all duration-300 select-none overflow-hidden"
        style={{
          borderColor: `rgba(${c}, 0.15)`,
          maxHeight: open ? '600px' : '0px',
          opacity: open ? 1 : 0,
          padding: open ? '20px' : '0 20px',
        }}
      >
        {/* Title input */}
        <div className="mb-5">
          <label className="block text-[10px] font-mono tracking-[0.3em] uppercase mb-2" style={{ color: `rgba(${c}, 0.4)` }}>
            Display Title
          </label>
          <input
            type="text"
            value={localTitle}
            onChange={(e) => handleTitleChange(e.target.value)}
            maxLength={20}
            className="w-full bg-transparent border px-3 py-2 font-mono text-sm outline-none transition-colors"
            style={{ borderColor: `rgba(${c}, 0.2)`, color: `rgb(${c})`, caretColor: `rgb(${c})` }}
            onFocus={(e) => { e.target.style.borderColor = `rgba(${c}, 0.5)`; }}
            onBlur={(e) => { e.target.style.borderColor = `rgba(${c}, 0.2)`; }}
          />
        </div>

        {/* Fun fact / subtitle input */}
        <div className="mb-5">
          <label className="block text-[10px] font-mono tracking-[0.3em] uppercase mb-2" style={{ color: `rgba(${c}, 0.4)` }}>
            Subtitle Text
          </label>
          <input
            type="text"
            value={localFact}
            onChange={(e) => handleFactChange(e.target.value)}
            placeholder="Leave empty for auto-rotate"
            maxLength={80}
            className="w-full bg-transparent border px-3 py-2 font-mono text-xs outline-none transition-colors"
            style={{ borderColor: `rgba(${c}, 0.2)`, color: `rgb(${c})`, caretColor: `rgb(${c})` }}
            onFocus={(e) => { e.target.style.borderColor = `rgba(${c}, 0.5)`; }}
            onBlur={(e) => { e.target.style.borderColor = `rgba(${c}, 0.2)`; }}
          />
          <p className="mt-1 text-[9px] font-mono" style={{ color: `rgba(${c}, 0.2)` }}>
            Empty = cycles through 10 built-in facts
          </p>
        </div>

        {/* Color picker */}
        <div className="mb-5">
          <label className="block text-[10px] font-mono tracking-[0.3em] uppercase mb-2" style={{ color: `rgba(${c}, 0.4)` }}>
            Theme Color
          </label>
          <div className="flex items-center gap-3 mb-3">
            <div className="relative w-8 h-8 border overflow-hidden" style={{ borderColor: `rgba(${c}, 0.3)` }}>
              <input
                type="color"
                value={theme.hex}
                onChange={(e) => setHex(e.target.value)}
                className="absolute inset-0 w-full h-full opacity-0"
                style={{ cursor: 'none' }}
              />
              <div className="w-full h-full" style={{ backgroundColor: theme.hex }} />
            </div>
            <span className="font-mono text-xs uppercase" style={{ color: `rgba(${c}, 0.5)` }}>
              {theme.hex}
            </span>
          </div>
          <div className="grid grid-cols-4 gap-2">
            {PRESETS.map((p) => (
              <button
                key={p.hex}
                data-hover
                onClick={() => setHex(p.hex)}
                className="h-7 border transition-all duration-200 relative"
                style={{
                  backgroundColor: p.hex + '22',
                  borderColor: theme.hex === p.hex ? p.hex : p.hex + '33',
                  boxShadow: theme.hex === p.hex ? `0 0 10px ${p.hex}33` : 'none',
                }}
                title={p.name}
              >
                <span className="text-[8px] font-mono opacity-60" style={{ color: p.hex }}>{p.name}</span>
              </button>
            ))}
          </div>
        </div>

        {/* Blur toggle */}
        <div>
          <label className="block text-[10px] font-mono tracking-[0.3em] uppercase mb-2" style={{ color: `rgba(${c}, 0.4)` }}>
            Grid Blur Overlay
          </label>
          <button
            data-hover
            onClick={() => setBlurEnabled(!theme.blurEnabled)}
            className="flex items-center gap-3 w-full px-3 py-2 border transition-all duration-300"
            style={{
              borderColor: `rgba(${c}, ${theme.blurEnabled ? 0.4 : 0.15})`,
              backgroundColor: theme.blurEnabled ? `rgba(${c}, 0.08)` : 'transparent',
            }}
          >
            <div
              className="w-8 h-4 rounded-full relative transition-all duration-300 flex-shrink-0"
              style={{ backgroundColor: theme.blurEnabled ? `rgba(${c}, 0.3)` : 'rgba(255,255,255,0.05)' }}
            >
              <div
                className="absolute top-0.5 w-3 h-3 rounded-full transition-all duration-300"
                style={{
                  left: theme.blurEnabled ? '18px' : '2px',
                  backgroundColor: theme.blurEnabled ? `rgb(${c})` : 'rgba(255,255,255,0.2)',
                  boxShadow: theme.blurEnabled ? `0 0 8px rgba(${c}, 0.5)` : 'none',
                }}
              />
            </div>
            <span className="font-mono text-xs" style={{ color: `rgba(${c}, 0.5)` }}>
              {theme.blurEnabled ? 'Enabled' : 'Disabled'}
            </span>
          </button>
        </div>
      </div>
    </>
  );
}
