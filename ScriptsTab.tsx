import { useState, useRef, useEffect, useCallback, memo } from 'react';
import { useTheme } from '../../ThemeContext';

interface Script {
  id: string;
  name: string;
  running: boolean;
}

// ═══════════════════════════════════════════
//  Extracted stable ScriptRow (no focus loss)
// ═══════════════════════════════════════════

const ScriptRow = memo(function ScriptRow({
  script, c, isEditing, editValue,
  onToggle, onStartRename, onEditChange, onCommitRename, onCancelRename,
}: {
  script: Script; c: string; isCustom?: boolean;
  isEditing: boolean; editValue: string;
  onToggle: () => void;
  onStartRename: () => void;
  onEditChange: (val: string) => void;
  onCommitRename: () => void;
  onCancelRename: () => void;
}) {
  const inputRef = useRef<HTMLInputElement>(null);

  useEffect(() => {
    if (isEditing && inputRef.current) {
      inputRef.current.focus();
      inputRef.current.select();
    }
  }, [isEditing]);

  return (
    <div
      className="flex items-center justify-between px-4 py-3 border mb-2 transition-all duration-200"
      style={{
        borderColor: script.running ? `rgba(${c}, 0.35)` : `rgba(${c}, 0.1)`,
        backgroundColor: script.running ? `rgba(${c}, 0.05)` : 'rgba(0,0,0,0.15)',
      }}
    >
      <div className="flex items-center gap-3 flex-1 min-w-0 mr-3">
        <div
          className="w-2 h-2 rounded-full transition-all duration-300 shrink-0"
          style={{
            backgroundColor: script.running ? `rgb(${c})` : `rgba(${c}, 0.2)`,
            boxShadow: script.running ? `0 0 8px rgba(${c}, 0.5)` : 'none',
          }}
        />
        {isEditing ? (
          <input
            ref={inputRef}
            type="text"
            value={editValue}
            onChange={(e) => onEditChange(e.target.value)}
            onBlur={onCommitRename}
            onKeyDown={(e) => {
              if (e.key === 'Enter') onCommitRename();
              if (e.key === 'Escape') onCancelRename();
            }}
            className="flex-1 bg-transparent font-mono text-xs outline-none border-b min-w-0"
            style={{
              color: `rgb(${c})`,
              borderColor: `rgba(${c}, 0.4)`,
              caretColor: `rgb(${c})`,
            }}
          />
        ) : (
          <span
            className="font-mono text-xs truncate cursor-pointer transition-colors"
            style={{ color: `rgba(${c}, 0.7)` }}
            onDoubleClick={onStartRename}
            title="Double-click to rename"
          >
            {script.name}
          </span>
        )}
        {!isEditing && (
          <button
            onClick={onStartRename}
            className="shrink-0 transition-opacity"
            style={{ color: `rgba(${c}, 0.3)` }}
            onMouseEnter={(e) => { e.currentTarget.style.color = `rgba(${c}, 0.7)`; }}
            onMouseLeave={(e) => { e.currentTarget.style.color = `rgba(${c}, 0.3)`; }}
            title="Rename"
          >
            <svg width="10" height="10" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
              <path d="M17 3a2.85 2.85 0 0 1 4 4L7.5 20.5 2 22l1.5-5.5Z" />
            </svg>
          </button>
        )}
      </div>
      <button
        onClick={onToggle}
        className="px-4 py-1.5 font-mono text-[10px] tracking-wider uppercase border transition-all duration-200 shrink-0"
        style={{
          borderColor: script.running ? `rgba(${c}, 0.5)` : `rgba(${c}, 0.25)`,
          color: script.running ? `rgb(${c})` : `rgba(${c}, 0.5)`,
          backgroundColor: script.running ? `rgba(${c}, 0.1)` : 'transparent',
        }}
        onMouseEnter={(e) => {
          e.currentTarget.style.backgroundColor = `rgba(${c}, 0.15)`;
          e.currentTarget.style.borderColor = `rgba(${c}, 0.6)`;
          e.currentTarget.style.color = `rgb(${c})`;
        }}
        onMouseLeave={(e) => {
          e.currentTarget.style.backgroundColor = script.running ? `rgba(${c}, 0.1)` : 'transparent';
          e.currentTarget.style.borderColor = script.running ? `rgba(${c}, 0.5)` : `rgba(${c}, 0.25)`;
          e.currentTarget.style.color = script.running ? `rgb(${c})` : `rgba(${c}, 0.5)`;
        }}
      >
        {script.running ? 'Stop' : 'Launch'}
      </button>
    </div>
  );
});

// ═══════════════════════════════════════════
//  Main ScriptsTab
// ═══════════════════════════════════════════

export default function ScriptsTab() {
  const { theme } = useTheme();
  const c = `${theme.r}, ${theme.g}, ${theme.b}`;

  const [scripts, setScripts] = useState<Script[]>([
    { id: '1', name: 'Placeholder Script 1', running: false },
    { id: '2', name: 'Placeholder Script 2', running: false },
    { id: '3', name: 'Placeholder Script 3', running: false },
    { id: '4', name: 'Placeholder Script 4', running: false },
  ]);

  const [customScripts, setCustomScripts] = useState<Script[]>([]);
  const [editingId, setEditingId] = useState<string | null>(null);
  const [editValue, setEditValue] = useState('');

  const runningCount = scripts.filter(s => s.running).length + customScripts.filter(s => s.running).length;

  const toggleScript = useCallback((id: string, isCustom: boolean) => {
    if (isCustom) {
      setCustomScripts(prev => prev.map(s => s.id === id ? { ...s, running: !s.running } : s));
    } else {
      setScripts(prev => prev.map(s => s.id === id ? { ...s, running: !s.running } : s));
    }
    window.dispatchEvent(new CustomEvent('seiware:script:toggle', { detail: { id, isCustom } }));
  }, []);

  const startRename = useCallback((script: Script) => {
    setEditingId(script.id);
    setEditValue(script.name);
  }, []);

  const commitRename = useCallback((id: string, isCustom: boolean) => {
    setEditingId(prev => {
      if (prev !== id) return prev; // not editing this one
      return null;
    });
    const newName = editValue.trim() || 'Unnamed Script';
    if (isCustom) {
      setCustomScripts(prev => prev.map(s => s.id === id ? { ...s, name: newName } : s));
    } else {
      setScripts(prev => prev.map(s => s.id === id ? { ...s, name: newName } : s));
    }
    window.dispatchEvent(new CustomEvent('seiware:script:rename', { detail: { id, name: newName, isCustom } }));
  }, [editValue]);

  const cancelRename = useCallback(() => {
    setEditingId(null);
  }, []);

  const addCustomScript = useCallback(() => {
    const newId = `custom_${Date.now()}`;
    setCustomScripts(prev => [...prev, { id: newId, name: `Custom Script ${prev.length + 1}`, running: false }]);
    window.dispatchEvent(new CustomEvent('seiware:script:add', { detail: { id: newId } }));
  }, []);

  return (
    <div className="space-y-6">
      {/* Scripts Section */}
      <div>
        <div className="flex items-center justify-between mb-4">
          <h3 className="font-mono text-xs tracking-[0.2em] uppercase" style={{ color: `rgba(${c}, 0.5)` }}>
            Scripts
          </h3>
          <div className="flex items-center gap-2">
            <span className="font-mono text-[10px]" style={{ color: `rgba(${c}, 0.3)` }}>Running:</span>
            <span
              className="font-mono text-xs font-bold px-2 py-0.5 border"
              style={{
                borderColor: runningCount > 0 ? `rgba(${c}, 0.4)` : `rgba(${c}, 0.15)`,
                color: runningCount > 0 ? `rgb(${c})` : `rgba(${c}, 0.3)`,
                backgroundColor: runningCount > 0 ? `rgba(${c}, 0.08)` : 'transparent',
              }}
            >
              {runningCount}
            </span>
          </div>
        </div>

        <div>
          {scripts.map(script => (
            <ScriptRow
              key={script.id} script={script} c={c} isCustom={false}
              isEditing={editingId === script.id} editValue={editingId === script.id ? editValue : ''}
              onToggle={() => toggleScript(script.id, false)}
              onStartRename={() => startRename(script)}
              onEditChange={setEditValue}
              onCommitRename={() => commitRename(script.id, false)}
              onCancelRename={cancelRename}
            />
          ))}
        </div>

        <p className="font-mono text-[9px] mt-2" style={{ color: `rgba(${c}, 0.2)` }}>
          Double-click a name or hit the pencil to rename
        </p>
      </div>

      {/* Custom Scripts Section */}
      <div>
        <h3 className="font-mono text-xs tracking-[0.2em] uppercase mb-4" style={{ color: `rgba(${c}, 0.5)` }}>
          Custom Scripts
        </h3>

        {customScripts.length > 0 && (
          <div className="mb-3">
            {customScripts.map(script => (
              <ScriptRow
                key={script.id} script={script} c={c} isCustom={true}
                isEditing={editingId === script.id} editValue={editingId === script.id ? editValue : ''}
                onToggle={() => toggleScript(script.id, true)}
                onStartRename={() => startRename(script)}
                onEditChange={setEditValue}
                onCommitRename={() => commitRename(script.id, true)}
                onCancelRename={cancelRename}
              />
            ))}
          </div>
        )}

        <button
          onClick={addCustomScript}
          className="w-full px-4 py-3 font-mono text-xs tracking-wider uppercase border border-dashed transition-all duration-200 flex items-center justify-center gap-2"
          style={{ borderColor: `rgba(${c}, 0.15)`, color: `rgba(${c}, 0.35)` }}
          onMouseEnter={(e) => {
            e.currentTarget.style.borderColor = `rgba(${c}, 0.35)`;
            e.currentTarget.style.color = `rgba(${c}, 0.7)`;
            e.currentTarget.style.backgroundColor = `rgba(${c}, 0.05)`;
          }}
          onMouseLeave={(e) => {
            e.currentTarget.style.borderColor = `rgba(${c}, 0.15)`;
            e.currentTarget.style.color = `rgba(${c}, 0.35)`;
            e.currentTarget.style.backgroundColor = 'transparent';
          }}
        >
          <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
            <path d="M12 5v14M5 12h14" />
          </svg>
          Add Custom Script
        </button>
      </div>
    </div>
  );
}
