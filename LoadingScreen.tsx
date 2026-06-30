import { useEffect, useState } from 'react';
import { useTheme } from '../ThemeContext';

export default function LoadingScreen({ onDone }: { onDone: () => void }) {
  const { theme } = useTheme();
  const [progress, setProgress] = useState(0);
  const [fadeOut, setFadeOut] = useState(false);

  useEffect(() => {
    let p = 0;
    const interval = setInterval(() => {
      p += Math.random() * 15 + 5;
      if (p >= 100) {
        p = 100;
        setProgress(100);
        clearInterval(interval);
        setTimeout(() => setFadeOut(true), 300);
        setTimeout(() => onDone(), 800);
      } else {
        setProgress(Math.floor(p));
      }
    }, 120);
    return () => clearInterval(interval);
  }, [onDone]);

  const c = `${theme.r}, ${theme.g}, ${theme.b}`;

  return (
    <div
      className="fixed inset-0 z-[10000] flex flex-col items-center justify-center bg-black select-none transition-opacity duration-500"
      style={{ opacity: fadeOut ? 0 : 1 }}
    >
      {/* Pulsing dot */}
      <div
        className="w-4 h-4 rounded-full mb-10 animate-pulse"
        style={{
          backgroundColor: `rgb(${c})`,
          boxShadow: `0 0 20px rgba(${c}, 0.6), 0 0 60px rgba(${c}, 0.2)`,
        }}
      />

      {/* Title */}
      <div
        className="text-3xl font-black tracking-[0.4em] font-mono mb-8"
        style={{ color: `rgb(${c})` }}
      >
        {theme.title}_
      </div>

      {/* Progress bar */}
      <div className="w-64 h-[2px] bg-white/5 relative overflow-hidden rounded-full">
        <div
          className="h-full transition-all duration-200 rounded-full"
          style={{
            width: `${progress}%`,
            backgroundColor: `rgb(${c})`,
            boxShadow: `0 0 10px rgba(${c}, 0.5)`,
          }}
        />
      </div>

      {/* Status text */}
      <div className="mt-4 font-mono text-xs tracking-[0.3em] uppercase" style={{ color: `rgba(${c}, 0.4)` }}>
        {progress < 30 ? 'Initializing grid...' : progress < 60 ? 'Loading vertices...' : progress < 90 ? 'Connecting nodes...' : 'System ready'}
      </div>

      <div className="mt-2 font-mono text-xs" style={{ color: `rgba(${c}, 0.2)` }}>
        {progress}%
      </div>
    </div>
  );
}
