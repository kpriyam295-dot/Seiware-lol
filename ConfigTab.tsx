import { useState, useCallback, memo } from 'react';
import { useTheme } from '../../ThemeContext';

interface PathConfig {
  id: string;
  label: string;
  value: string;
}

// ═══════════════════════════════════════════
//  Extracted stable components (no focus loss)
// ═══════════════════════════════════════════

const PathRow = memo(function PathRow({
  config, c, isSaved, onChange, onSave,
}: {
  config: PathConfig; c: string; isSaved: boolean;
  onChange: (id: string, value: string) => void;
  onSave: (id: string) => void;
}) {
  return (
    <div className="flex items-center gap-2 mb-2">
      <div
        className="flex-1 flex items-center border transition-all duration-200"
        style={{ borderColor: `rgba(${c}, 0.1)`, backgroundColor: 'rgba(0,0,0,0.15)' }}
      >
        <span
          className="px-3 py-2 font-mono text-[10px] uppercase tracking-wider border-r whitespace-nowrap"
          style={{ borderColor: `rgba(${c}, 0.1)`, color: `rgba(${c}, 0.35)`, minWidth: '110px' }}
        >
          {config.label}
        </span>
        <input
          type="text"
          defaultValue={config.value}
          onChange={(e) => onChange(config.id, e.target.value)}
          className="flex-1 bg-transparent px-3 py-2 font-mono text-xs outline-none min-w-0"
          style={{ color: `rgba(${c}, 0.7)`, caretColor: `rgb(${c})` }}
        />
      </div>
      <button
        onClick={() => onSave(config.id)}
        className="px-3 py-2 font-mono text-[10px] uppercase tracking-wider border transition-all duration-200 whitespace-nowrap"
        style={{
          borderColor: isSaved ? `rgba(34, 197, 94, 0.4)` : `rgba(${c}, 0.15)`,
          color: isSaved ? `rgb(34, 197, 94)` : `rgba(${c}, 0.45)`,
          backgroundColor: isSaved ? `rgba(34, 197, 94, 0.1)` : 'transparent',
        }}
        onMouseEnter={(e) => {
          if (!isSaved) {
            e.currentTarget.style.borderColor = `rgba(${c}, 0.4)`;
            e.currentTarget.style.color = `rgb(${c})`;
            e.currentTarget.style.backgroundColor = `rgba(${c}, 0.08)`;
          }
        }}
        onMouseLeave={(e) => {
          if (!isSaved) {
            e.currentTarget.style.borderColor = `rgba(${c}, 0.15)`;
            e.currentTarget.style.color = `rgba(${c}, 0.45)`;
            e.currentTarget.style.backgroundColor = 'transparent';
          }
        }}
      >
        {isSaved ? '✓ Saved' : 'Save'}
      </button>
    </div>
  );
});

function Toggle({ label, c, enabled, onChange }: { label: string; c: string; enabled: boolean; onChange: (v: boolean) => void }) {
  return (
    <div
      className="flex items-center justify-between px-4 py-3 border mb-2 transition-all duration-200"
      style={{
        borderColor: `rgba(${c}, ${enabled ? 0.25 : 0.1})`,
        backgroundColor: enabled ? `rgba(${c}, 0.04)` : 'rgba(0,0,0,0.1)',
      }}
    >
      <span className="font-mono text-xs" style={{ color: `rgba(${c}, 0.6)` }}>{label}</span>
      <button onClick={() => onChange(!enabled)} className="flex items-center gap-2">
        <div
          className="w-10 h-5 rounded-full relative transition-all duration-300"
          style={{ backgroundColor: enabled ? `rgba(${c}, 0.3)` : 'rgba(255,255,255,0.05)' }}
        >
          <div
            className="absolute top-0.5 w-4 h-4 rounded-full transition-all duration-300"
            style={{
              left: enabled ? '22px' : '2px',
              backgroundColor: enabled ? `rgb(${c})` : 'rgba(255,255,255,0.2)',
              boxShadow: enabled ? `0 0 8px rgba(${c}, 0.5)` : 'none',
            }}
          />
        </div>
        <span className="font-mono text-[10px] uppercase tracking-wider w-8" style={{ color: enabled ? `rgb(${c})` : `rgba(${c}, 0.3)` }}>
          {enabled ? 'ON' : 'OFF'}
        </span>
      </button>
    </div>
  );
}

function CfgBtn({ label, icon, c, onClick }: { label: string; icon: React.ReactNode; c: string; onClick: () => void }) {
  return (
    <button
      onClick={onClick}
      className="flex items-center gap-2 px-5 py-2.5 font-mono text-[10px] tracking-wider uppercase border transition-all duration-200"
      style={{ borderColor: `rgba(${c}, 0.2)`, color: `rgba(${c}, 0.55)` }}
      onMouseEnter={(e) => {
        e.currentTarget.style.borderColor = `rgba(${c}, 0.45)`;
        e.currentTarget.style.color = `rgb(${c})`;
        e.currentTarget.style.backgroundColor = `rgba(${c}, 0.08)`;
      }}
      onMouseLeave={(e) => {
        e.currentTarget.style.borderColor = `rgba(${c}, 0.2)`;
        e.currentTarget.style.color = `rgba(${c}, 0.55)`;
        e.currentTarget.style.backgroundColor = 'transparent';
      }}
    >
      {icon}
      {label}
    </button>
  );
}

// ═══════════════════════════════════════════
//  Main ConfigTab
// ═══════════════════════════════════════════

export default function ConfigTab() {
  const { theme } = useTheme();
  const c = `${theme.r}, ${theme.g}, ${theme.b}`;

  const [interceptEnabled, setInterceptEnabled] = useState(false);
  const [startWithWindows, setStartWithWindows] = useState(false);
  const [copiedAll, setCopiedAll] = useState(false);
  const [savedAll, setSavedAll] = useState(false);
  const [savedIds, setSavedIds] = useState<Set<string>>(new Set());

  const [paths, setPaths] = useState<PathConfig[]>([
    { id: 'dreamland', label: 'DreamLand.exe', value: 'C:\\Seiware\\DreamLand.exe' },
    { id: 'terminal', label: 'Terminal.exe', value: 'C:\\Seiware\\Terminal.exe' },
    { id: 'powershell', label: 'Windows PowerShell.exe', value: 'C:\\Seiware\\Windows PowerShell.exe' },
    { id: 'launcher', label: 'Launcher.exe', value: 'C:\\Seiware\\Launcher.exe' },
    { id: 'terminal_ico', label: 'terminal.ico', value: 'C:\\Seiware\\Assets\\terminal.ico' },
    { id: 'powershell_ico', label: 'powershell.ico', value: 'C:\\Seiware\\Assets\\powershell.ico' },
    { id: 'config', label: 'config.json', value: 'C:\\Seiware\\config.json' },
    { id: 'scripts_dir', label: 'Scripts Directory', value: 'C:\\Seiware\\Scripts\\' },
    { id: 'worker', label: 'worker.ps1', value: 'C:\\Seiware\\worker.ps1' },
    { id: 'memory_optimizer', label: 'Memory_Optimizer.ps1', value: 'C:\\Seiware\\Memory_Optimizer.ps1' },
    { id: 'lol', label: 'LOL.ps1', value: 'C:\\Seiware\\LOL.ps1' },
    { id: 'hidefiles', label: 'HideFiles.ps1', value: 'C:\\Seiware\\HideFiles.ps1' },
    { id: 'cmd_shortcut', label: 'CMD Shortcut', value: 'C:\\Seiware\\Command Prompt.exe' },
    { id: 'ps_shortcut', label: 'PS Shortcut', value: 'C:\\Seiware\\Windows PowerShell.exe' },
  ]);

  const handleConfigAction = useCallback((action: string) => {
    window.dispatchEvent(new CustomEvent('seiware:config:action', { detail: { action } }));
  }, []);

  const handleToggle = useCallback((setting: string, value: boolean) => {
    window.dispatchEvent(new CustomEvent('seiware:config:toggle', { detail: { setting, value } }));
  }, []);

  // Only updates internal ref — does NOT cause re-render (no setState)
  const pathsRef = useCallback((id: string, value: string) => {
    setPaths(prev => prev.map(p => p.id === id ? { ...p, value } : p));
  }, []);

  const handlePathSave = useCallback((id: string) => {
    // Read latest from state at save-time
    setPaths(prev => {
      const path = prev.find(p => p.id === id);
      window.dispatchEvent(new CustomEvent('seiware:config:path:save', { detail: { id, path: path?.value } }));
      return prev;
    });
    setSavedIds(prev => new Set(prev).add(id));
    setTimeout(() => setSavedIds(prev => { const n = new Set(prev); n.delete(id); return n; }), 1500);
  }, []);

  const handleCopyAllPaths = useCallback(() => {
    setPaths(prev => {
      const pathsText = prev.map(p => `${p.label}: ${p.value}`).join('\n');
      navigator.clipboard.writeText(pathsText);
      window.dispatchEvent(new CustomEvent('seiware:config:paths:copy', { detail: { paths: prev } }));
      return prev;
    });
    setCopiedAll(true);
    setTimeout(() => setCopiedAll(false), 1500);
  }, []);

  const handleSaveAllPaths = useCallback(() => {
    setPaths(prev => {
      const allPaths: Record<string, string> = {};
      prev.forEach(p => { allPaths[p.id] = p.value; });
      window.dispatchEvent(new CustomEvent('seiware:config:paths:saveall', { detail: { paths: allPaths } }));
      return prev;
    });
    // Flash all rows as saved
    setPaths(prev => {
      const allIds = new Set(prev.map(p => p.id));
      setSavedIds(allIds);
      return prev;
    });
    setSavedAll(true);
    setTimeout(() => { setSavedAll(false); setSavedIds(new Set()); }, 1500);
  }, []);

  return (
    <div className="space-y-6">
      {/* Configuration Actions */}
      <div>
        <h3 className="font-mono text-xs tracking-[0.2em] uppercase mb-4" style={{ color: `rgba(${c}, 0.5)` }}>
          Configuration
        </h3>
        <div className="flex items-center gap-3 flex-wrap">
          <CfgBtn c={c} label="Open Config"
            icon={<svg width="11" height="11" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2"><path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8Z"/><path d="M14 2v6h6"/></svg>}
            onClick={() => handleConfigAction('open_config')}
          />
          <CfgBtn c={c} label="Setup Wizard"
            icon={<svg width="11" height="11" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2"><path d="m21.64 3.64-1.28-1.28a1.21 1.21 0 0 0-1.72 0L2.36 18.64a1.21 1.21 0 0 0 0 1.72l1.28 1.28a1.2 1.2 0 0 0 1.72 0L21.64 5.36a1.2 1.2 0 0 0 0-1.72Z"/><path d="m14 7 3 3"/></svg>}
            onClick={() => handleConfigAction('setup_wizard')}
          />
          <CfgBtn c={c} label="Banned Names"
            icon={<svg width="11" height="11" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2"><circle cx="12" cy="12" r="10"/><path d="m4.93 4.93 14.14 14.14"/></svg>}
            onClick={() => handleConfigAction('banned_names')}
          />
        </div>
      </div>

      {/* Toggles */}
      <div>
        <h3 className="font-mono text-xs tracking-[0.2em] uppercase mb-4" style={{ color: `rgba(${c}, 0.5)` }}>
          Settings
        </h3>
        <Toggle c={c} label="Intercept" enabled={interceptEnabled}
          onChange={(v) => { setInterceptEnabled(v); handleToggle('intercept', v); }}
        />
        <Toggle c={c} label="Start with Windows" enabled={startWithWindows}
          onChange={(v) => { setStartWithWindows(v); handleToggle('start_with_windows', v); }}
        />
      </div>

      {/* Component Paths */}
      <div>
        <div className="flex items-center justify-between mb-4">
          <h3 className="font-mono text-xs tracking-[0.2em] uppercase" style={{ color: `rgba(${c}, 0.5)` }}>
            Component Paths
          </h3>
          <div className="flex items-center gap-2">
            <button
              onClick={handleCopyAllPaths}
              className="flex items-center gap-2 px-3 py-1.5 font-mono text-[10px] uppercase tracking-wider border transition-all duration-200"
              style={{
                borderColor: copiedAll ? `rgba(34, 197, 94, 0.4)` : `rgba(${c}, 0.15)`,
                color: copiedAll ? `rgb(34, 197, 94)` : `rgba(${c}, 0.45)`,
                backgroundColor: copiedAll ? `rgba(34, 197, 94, 0.08)` : 'transparent',
              }}
              onMouseEnter={(e) => {
                if (!copiedAll) {
                  e.currentTarget.style.borderColor = `rgba(${c}, 0.4)`;
                  e.currentTarget.style.color = `rgb(${c})`;
                  e.currentTarget.style.backgroundColor = `rgba(${c}, 0.08)`;
                }
              }}
              onMouseLeave={(e) => {
                if (!copiedAll) {
                  e.currentTarget.style.borderColor = `rgba(${c}, 0.15)`;
                  e.currentTarget.style.color = `rgba(${c}, 0.45)`;
                  e.currentTarget.style.backgroundColor = 'transparent';
                }
              }}
            >
              <svg width="10" height="10" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
                {copiedAll ? (
                  <path d="M20 6L9 17l-5-5" />
                ) : (
                  <>
                    <rect x="9" y="9" width="13" height="13" rx="2" />
                    <path d="M5 15H4a2 2 0 01-2-2V4a2 2 0 012-2h9a2 2 0 012 2v1" />
                  </>
                )}
              </svg>
              {copiedAll ? 'Copied!' : 'Copy All Paths'}
            </button>
            <button
              onClick={handleSaveAllPaths}
              className="flex items-center gap-2 px-3 py-1.5 font-mono text-[10px] uppercase tracking-wider border transition-all duration-200"
              style={{
                borderColor: savedAll ? `rgba(34, 197, 94, 0.4)` : `rgba(${c}, 0.25)`,
                color: savedAll ? `rgb(34, 197, 94)` : `rgba(${c}, 0.55)`,
                backgroundColor: savedAll ? `rgba(34, 197, 94, 0.08)` : 'transparent',
              }}
              onMouseEnter={(e) => {
                if (!savedAll) {
                  e.currentTarget.style.borderColor = `rgba(${c}, 0.5)`;
                  e.currentTarget.style.color = `rgb(${c})`;
                  e.currentTarget.style.backgroundColor = `rgba(${c}, 0.08)`;
                }
              }}
              onMouseLeave={(e) => {
                if (!savedAll) {
                  e.currentTarget.style.borderColor = `rgba(${c}, 0.25)`;
                  e.currentTarget.style.color = `rgba(${c}, 0.55)`;
                  e.currentTarget.style.backgroundColor = 'transparent';
                }
              }}
            >
              <svg width="10" height="10" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
                {savedAll ? (
                  <path d="M20 6L9 17l-5-5" />
                ) : (
                  <path d="M19 21H5a2 2 0 01-2-2V5a2 2 0 012-2h11l5 5v11a2 2 0 01-2 2zM17 21v-8H7v8M7 3v5h8" />
                )}
              </svg>
              {savedAll ? '✓ All Saved' : 'Save All Paths'}
            </button>
          </div>
        </div>

        <div>
          {paths.map(p => (
            <PathRow key={p.id} config={p} c={c} isSaved={savedIds.has(p.id)} onChange={pathsRef} onSave={handlePathSave} />
          ))}
        </div>
      </div>
    </div>
  );
}
