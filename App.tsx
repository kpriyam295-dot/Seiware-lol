import { useState, useEffect, useCallback, useRef } from 'react';
import { ThemeProvider, useTheme } from './ThemeContext';
import InteractiveGrid from './components/InteractiveGrid';
import HeroContent from './components/HeroContent';
import ControlPanel from './components/ControlPanel';
import LoadingScreen from './components/LoadingScreen';
import TabPanel, { TabName } from './components/TabPanel';

function NexusApp() {
  const { theme } = useTheme();
  const [loaded, setLoaded] = useState(false);
  const [activeTab, setActiveTab] = useState<TabName>(null);
  const cursorRef = useRef<HTMLDivElement>(null);

  const c = `${theme.r}, ${theme.g}, ${theme.b}`;

  const handleDone = useCallback(() => setLoaded(true), []);
  const handleTabOpen = useCallback((tab: TabName) => setActiveTab(tab), []);
  const handleTabClose = useCallback(() => setActiveTab(null), []);

  // Direct DOM cursor — no React re-renders
  useEffect(() => {
    const onMove = (e: MouseEvent) => {
      if (cursorRef.current) {
        cursorRef.current.style.left = (e.clientX - 12) + 'px';
        cursorRef.current.style.top = (e.clientY - 12) + 'px';
      }
    };
    window.addEventListener('mousemove', onMove, { passive: true });
    return () => window.removeEventListener('mousemove', onMove);
  }, []);

  // ══════════════════════════════════════════════
  //  C# / .NET INTEGRATION BRIDGE
  // ══════════════════════════════════════════════
  useEffect(() => {
    const bridge = {
      // Tab control
      openTab: (tab: TabName) => setActiveTab(tab),
      closeTab: () => setActiveTab(null),
      getActiveTab: () => activeTab,

      // Emit to backend — WebView2 postMessage
      _send: (type: string, data: unknown) => {
        try {
          // WebView2
          if ((window as any).chrome?.webview) {
            (window as any).chrome.webview.postMessage(JSON.stringify({ type, data }));
            return;
          }
          // CefSharp
          if ((window as any).CefSharp) {
            (window as any).CefSharp.PostMessage(JSON.stringify({ type, data }));
            return;
          }
          // window.external (legacy WebBrowser)
          if ((window as any).external?.SendMessage) {
            (window as any).external.SendMessage(JSON.stringify({ type, data }));
            return;
          }
        } catch { /* not embedded */ }
      },
    };

    (window as any).SeiwareUI = bridge;

    // Auto-forward all seiware:* events to the C# backend
    const eventTypes = [
      'seiware:script:toggle', 'seiware:script:add', 'seiware:script:rename',
      'seiware:terminal:action',
      'seiware:config:action', 'seiware:config:toggle',
      'seiware:config:path:save', 'seiware:config:paths:copy',
      'seiware:config:paths:saveall',
    ];

    const handlers = eventTypes.map(type => {
      const handler = (e: Event) => {
        bridge._send(type, (e as CustomEvent).detail);
      };
      window.addEventListener(type, handler);
      return { type, handler };
    });

    return () => {
      handlers.forEach(({ type, handler }) => window.removeEventListener(type, handler));
    };
  }, [activeTab]);

  return (
    <div className="relative w-full h-screen overflow-hidden bg-black">
      {!loaded && <LoadingScreen onDone={handleDone} />}

      <InteractiveGrid />
      <HeroContent onTabOpen={handleTabOpen} />
      <ControlPanel />
      <TabPanel activeTab={activeTab} onClose={handleTabClose} />

      {/* Custom cursor — DOM-driven, no React re-renders */}
      <div
        ref={cursorRef}
        className="fixed z-[9999] pointer-events-none"
        style={{
          left: -100,
          top: -100,
          width: 24,
          height: 24,
          border: `1px solid rgba(${c}, 0.5)`,
          borderRadius: '50%',
          mixBlendMode: 'screen',
        }}
      />

      {/* Noise overlay */}
      <div
        className="fixed inset-0 z-[50] pointer-events-none opacity-[0.03]"
        style={{
          backgroundImage: `url("data:image/svg+xml,%3Csvg viewBox='0 0 256 256' xmlns='http://www.w3.org/2000/svg'%3E%3Cfilter id='n'%3E%3CfeTurbulence type='fractalNoise' baseFrequency='0.9' numOctaves='4' stitchTiles='stitch'/%3E%3C/filter%3E%3Crect width='100%25' height='100%25' filter='url(%23n)'/%3E%3C/svg%3E")`,
          backgroundRepeat: 'repeat',
          backgroundSize: '128px 128px',
        }}
      />
    </div>
  );
}

export default function App() {
  return (
    <ThemeProvider>
      <NexusApp />
    </ThemeProvider>
  );
}
