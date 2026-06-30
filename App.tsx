import { useState, useCallback } from 'react';
import { ThemeProvider, useTheme } from './ThemeContext';
import CursorFollower from './components/CursorFollower';
import InteractiveGrid from './components/InteractiveGrid';
import HeroContent from './components/HeroContent';
import ControlPanel from './components/ControlPanel';
import LoadingScreen from './components/LoadingScreen';

function AppInner() {
  const [loading, setLoading] = useState(true);
  const { theme } = useTheme();
  const c = `${theme.r}, ${theme.g}, ${theme.b}`;

  const handleLoadDone = useCallback(() => setLoading(false), []);

  return (
    <div className="relative min-h-screen bg-black overflow-hidden select-none">
      {/* Loading screen */}
      {loading && <LoadingScreen onDone={handleLoadDone} />}

      {/* Deep black gradient background */}
      <div className="fixed inset-0 z-0">
        <div className="absolute inset-0 bg-gradient-to-br from-black via-gray-950 to-black" />
        <div className="absolute inset-0" style={{ background: `linear-gradient(to top, rgba(${c}, 0.03), transparent)` }} />
        <div className="absolute inset-0" style={{ background: `radial-gradient(ellipse at center, rgba(${c}, 0.04) 0%, transparent 70%)` }} />
        <div className="absolute inset-0 bg-[radial-gradient(ellipse_at_center,transparent_40%,rgba(0,0,0,0.6)_100%)]" />
      </div>

      {/* Interactive grid canvas */}
      <InteractiveGrid />

      {/* Hero content (includes blur overlay if enabled) */}
      <HeroContent />

      {/* Control panel */}
      <ControlPanel />

      {/* Custom cursor */}
      <CursorFollower />

      {/* Noise texture overlay */}
      <div
        className="fixed inset-0 z-[100] pointer-events-none opacity-[0.015]"
        style={{
          backgroundImage: `url("data:image/svg+xml,%3Csvg viewBox='0 0 256 256' xmlns='http://www.w3.org/2000/svg'%3E%3Cfilter id='noiseFilter'%3E%3CfeTurbulence type='fractalNoise' baseFrequency='0.9' numOctaves='4' stitchTiles='stitch'/%3E%3C/filter%3E%3Crect width='100%25' height='100%25' filter='url(%23noiseFilter)'/%3E%3C/svg%3E")`,
          backgroundRepeat: 'repeat',
        }}
      />
    </div>
  );
}

export default function App() {
  return (
    <ThemeProvider>
      <AppInner />
    </ThemeProvider>
  );
}
