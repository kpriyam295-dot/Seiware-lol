import { useTheme } from '../ThemeContext';
import ScriptsTab from './tabs/ScriptsTab';
import TerminalTab from './tabs/TerminalTab';
import ConfigTab from './tabs/ConfigTab';

export type TabName = 'scripts' | 'terminal' | 'config' | null;

interface TabPanelProps {
  activeTab: TabName;
  onClose: () => void;
}

export default function TabPanel({ activeTab, onClose }: TabPanelProps) {
  const { theme } = useTheme();
  const c = `${theme.r}, ${theme.g}, ${theme.b}`;

  if (!activeTab) return null;

  const tabTitles: Record<string, string> = {
    scripts: 'Scripts',
    terminal: 'Terminal',
    config: 'Configuration',
  };

  return (
    <div className="fixed inset-0 z-[100] flex items-center justify-center pointer-events-auto">
      {/* Backdrop — subtle dark tint, lets grid bleed through */}
      <div
        className="absolute inset-0"
        style={{ backgroundColor: 'rgba(0, 0, 0, 0.4)' }}
        onClick={onClose}
      />

      {/* Panel — transparent with blur so grid shows through */}
      <div
        className="relative w-[90vw] max-w-4xl h-[80vh] max-h-[700px] border flex flex-col overflow-hidden"
        style={{
          borderColor: `rgba(${c}, 0.25)`,
          backgroundColor: 'rgba(0, 0, 0, 0.45)',
          backdropFilter: 'blur(20px)',
          WebkitBackdropFilter: 'blur(20px)',
          boxShadow: `0 0 60px rgba(0, 0, 0, 0.5), inset 0 0 60px rgba(0, 0, 0, 0.2), 0 0 1px rgba(${c}, 0.3)`,
        }}
      >
        {/* Header */}
        <div
          className="flex items-center justify-between px-6 py-4 border-b shrink-0"
          style={{
            borderColor: `rgba(${c}, 0.12)`,
            backgroundColor: `rgba(${c}, 0.03)`,
          }}
        >
          <div className="flex items-center gap-3">
            <div className="w-2 h-2 rotate-45" style={{ border: `1px solid rgba(${c}, 0.5)` }} />
            <h2
              className="font-mono text-sm tracking-[0.3em] uppercase"
              style={{
                color: 'transparent',
                WebkitTextStroke: `1px rgb(${c})`,
                textShadow: `0 0 20px rgba(${c}, 0.3)`,
              }}
            >
              {tabTitles[activeTab]}
            </h2>
          </div>
          <button
            onClick={onClose}
            className="w-8 h-8 flex items-center justify-center border transition-all duration-200"
            style={{ borderColor: `rgba(${c}, 0.2)`, color: `rgba(${c}, 0.5)` }}
            onMouseEnter={(e) => {
              e.currentTarget.style.borderColor = `rgba(${c}, 0.5)`;
              e.currentTarget.style.color = `rgb(${c})`;
              e.currentTarget.style.backgroundColor = `rgba(${c}, 0.1)`;
            }}
            onMouseLeave={(e) => {
              e.currentTarget.style.borderColor = `rgba(${c}, 0.2)`;
              e.currentTarget.style.color = `rgba(${c}, 0.5)`;
              e.currentTarget.style.backgroundColor = 'transparent';
            }}
          >
            <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
              <path d="M18 6L6 18M6 6l12 12" />
            </svg>
          </button>
        </div>

        {/* Content */}
        <div className="flex-1 overflow-y-auto p-6 custom-scrollbar min-h-0">
          {activeTab === 'scripts' && <ScriptsTab />}
          {activeTab === 'terminal' && <TerminalTab />}
          {activeTab === 'config' && <ConfigTab />}
        </div>
      </div>
    </div>
  );
}
