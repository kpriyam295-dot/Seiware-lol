import { useState } from 'react';
import { useTheme } from '../../ThemeContext';

export default function TerminalTab() {
  const { theme } = useTheme();
  const c = `${theme.r}, ${theme.g}, ${theme.b}`;
  const [sessionState, setSessionState] = useState<'idle' | 'running' | 'admin'>('idle');

  const handleAction = (action: string) => {
    if (action === 'start_session') setSessionState('running');
    if (action === 'end_session') setSessionState('idle');
    if (action === 'admin') setSessionState('admin');
    if (action === 'clear') {
      // Clear is handled by backend, state stays
    }
    window.dispatchEvent(new CustomEvent('seiware:terminal:action', { detail: { action } }));
  };

  const Btn = ({ label, actionKey, variant = 'default' }: { label: string; actionKey: string; variant?: 'default' | 'danger' | 'primary' | 'warn' }) => {
    const isActive = (actionKey === 'start_session' && sessionState === 'running') ||
                     (actionKey === 'admin' && sessionState === 'admin');

    const base = (() => {
      if (variant === 'danger') return {
        border: `rgba(239, 68, 68, 0.3)`, borderH: `rgba(239, 68, 68, 0.6)`,
        color: `rgba(239, 68, 68, 0.7)`, colorH: `rgb(239, 68, 68)`,
        bg: 'transparent', bgH: `rgba(239, 68, 68, 0.12)`,
      };
      if (variant === 'primary') return {
        border: isActive ? `rgba(${c}, 0.6)` : `rgba(${c}, 0.35)`,
        borderH: `rgba(${c}, 0.7)`,
        color: `rgb(${c})`, colorH: `rgb(${c})`,
        bg: isActive ? `rgba(${c}, 0.12)` : `rgba(${c}, 0.06)`,
        bgH: `rgba(${c}, 0.2)`,
      };
      if (variant === 'warn') return {
        border: isActive ? `rgba(251, 191, 36, 0.5)` : `rgba(251, 191, 36, 0.25)`,
        borderH: `rgba(251, 191, 36, 0.6)`,
        color: isActive ? `rgb(251, 191, 36)` : `rgba(251, 191, 36, 0.7)`,
        colorH: `rgb(251, 191, 36)`,
        bg: isActive ? `rgba(251, 191, 36, 0.1)` : 'transparent',
        bgH: `rgba(251, 191, 36, 0.15)`,
      };
      return {
        border: `rgba(${c}, 0.2)`, borderH: `rgba(${c}, 0.45)`,
        color: `rgba(${c}, 0.55)`, colorH: `rgba(${c}, 0.9)`,
        bg: 'transparent', bgH: `rgba(${c}, 0.08)`,
      };
    })();

    return (
      <button
        onClick={() => handleAction(actionKey)}
        className="px-5 py-2.5 font-mono text-[10px] tracking-wider uppercase border transition-all duration-200 flex items-center gap-2"
        style={{ borderColor: base.border, color: base.color, backgroundColor: base.bg }}
        onMouseEnter={(e) => {
          e.currentTarget.style.borderColor = base.borderH;
          e.currentTarget.style.color = base.colorH;
          e.currentTarget.style.backgroundColor = base.bgH;
        }}
        onMouseLeave={(e) => {
          e.currentTarget.style.borderColor = base.border;
          e.currentTarget.style.color = base.color;
          e.currentTarget.style.backgroundColor = base.bg;
        }}
      >
        {isActive && (
          <div className="w-1.5 h-1.5 rounded-full animate-pulse" style={{
            backgroundColor: variant === 'warn' ? 'rgb(251, 191, 36)' : `rgb(${c})`,
            boxShadow: variant === 'warn' ? '0 0 6px rgba(251, 191, 36, 0.5)' : `0 0 6px rgba(${c}, 0.5)`,
          }} />
        )}
        {label}
      </button>
    );
  };

  return (
    <div className="h-full flex flex-col">
      {/* Control Bar */}
      <div
        className="flex items-center gap-3 mb-4 pb-4 border-b flex-wrap shrink-0"
        style={{ borderColor: `rgba(${c}, 0.08)` }}
      >
        <Btn label="Start Session" actionKey="start_session" variant="primary" />
        <Btn label="End Session" actionKey="end_session" variant="danger" />
        <Btn label="Clear" actionKey="clear" />
        <Btn label="Admin" actionKey="admin" variant="warn" />
      </div>

      {/* Terminal Area — transparent, grid shows through */}
      <div
        className="flex-1 border relative overflow-hidden"
        style={{
          borderColor: `rgba(${c}, 0.12)`,
          backgroundColor: 'rgba(0, 0, 0, 0.3)',
          minHeight: '250px',
        }}
      >
        <div
          id="seiware-terminal-container"
          className="absolute inset-0"
          style={{ padding: '16px' }}
        >
          {/* Your existing terminal gets injected here by C# backend */}
          <div className="h-full flex flex-col font-mono text-xs leading-relaxed">
            <p style={{ color: `rgba(${c}, 0.5)` }}>
              <span style={{ color: `rgba(${c}, 0.7)` }}>
                {sessionState === 'admin' ? 'Administrator: C:\\Windows\\System32>' : 'C:\\Users\\User>'}
              </span>{' '}
              <span className="animate-pulse" style={{ color: `rgba(${c}, 0.4)` }}>_</span>
            </p>
            <p className="mt-6 text-[10px]" style={{ color: `rgba(${c}, 0.15)` }}>
              [ Terminal embed container — your FakeTerminal pipes here ]
            </p>
          </div>
        </div>

        {/* CRT scanline overlay */}
        <div
          className="absolute inset-0 pointer-events-none"
          style={{
            opacity: 0.04,
            background: `repeating-linear-gradient(0deg, transparent, transparent 2px, rgba(${c}, 0.05) 2px, rgba(${c}, 0.05) 4px)`,
          }}
        />
      </div>

      {/* Status bar */}
      <div
        className="flex items-center justify-between mt-3 pt-3 border-t shrink-0"
        style={{ borderColor: `rgba(${c}, 0.08)` }}
      >
        <div className="flex items-center gap-2">
          <div
            className="w-1.5 h-1.5 rounded-full"
            style={{
              backgroundColor: sessionState === 'idle' ? `rgba(${c}, 0.3)` : sessionState === 'admin' ? 'rgb(251, 191, 36)' : `rgb(${c})`,
              boxShadow: sessionState !== 'idle' ? `0 0 6px ${sessionState === 'admin' ? 'rgba(251, 191, 36, 0.5)' : `rgba(${c}, 0.5)`}` : 'none',
              animation: sessionState !== 'idle' ? 'pulse 2s infinite' : 'none',
            }}
          />
          <span className="font-mono text-[9px] uppercase tracking-wider" style={{
            color: sessionState === 'idle' ? `rgba(${c}, 0.3)` : sessionState === 'admin' ? 'rgba(251, 191, 36, 0.7)' : `rgba(${c}, 0.6)`,
          }}>
            {sessionState === 'idle' ? 'Session Idle' : sessionState === 'admin' ? 'Admin Session' : 'Session Active'}
          </span>
        </div>
        <span className="font-mono text-[9px]" style={{ color: `rgba(${c}, 0.2)` }}>
          PID: {sessionState === 'idle' ? '----' : Math.floor(Math.random() * 9000 + 1000)}
        </span>
      </div>
    </div>
  );
}
