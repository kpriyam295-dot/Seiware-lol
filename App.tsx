import { useState } from 'react';
import { 
  FileCode, 
  FolderOpen, 
  Copy, 
  Check,
  Terminal,
  Zap,
  Shield,
  Download,
  ChevronRight,
  Plus,
  Play,
  Trash2,
  Code2
} from 'lucide-react';
import { motion, AnimatePresence } from 'framer-motion';
import { cn } from './utils/cn';

interface CustomScript {
  name: string;
  description: string;
  path: string;
}

interface ComponentPath {
  name: string;
  path: string;
  exists: boolean;
  description: string;
}

const COMPONENT_PATHS: ComponentPath[] = [
  { name: 'DreamLand.exe', path: 'C:\\DreamLand\\DreamLand.exe', exists: true, description: 'Main application' },
  { name: 'Terminal.exe', path: 'C:\\DreamLand\\Terminal.exe', exists: true, description: 'Fake CMD terminal' },
  { name: 'Windows PowerShell.exe', path: 'C:\\DreamLand\\Windows PowerShell.exe', exists: true, description: 'Fake PowerShell terminal' },
  { name: 'DreamLandLauncher.exe', path: 'C:\\DreamLand\\DreamLandLauncher.exe', exists: true, description: 'Title-safe launcher' },
  { name: 'terminal.ico', path: 'C:\\DreamLand\\terminal.ico', exists: true, description: 'Terminal icon' },
  { name: 'powershell.ico', path: 'C:\\DreamLand\\powershell.ico', exists: true, description: 'PowerShell icon' },
  { name: 'Config Directory', path: '%APPDATA%\\DreamLand', exists: true, description: 'Configuration folder' },
  { name: 'config.json', path: '%APPDATA%\\DreamLand\\config.json', exists: true, description: 'Main configuration' },
  { name: 'Scripts Directory', path: '%APPDATA%\\DreamLand\\scripts', exists: true, description: 'PowerShell scripts' },
  { name: 'CMD Shortcut', path: 'C:\\Users\\{USER}\\AppData\\Roaming\\Microsoft\\Windows\\Start Menu\\Programs\\System Tools\\Command Prompt.lnk', exists: false, description: 'Start Menu shortcut' },
  { name: 'PowerShell Shortcut', path: 'C:\\Users\\{USER}\\AppData\\Roaming\\Microsoft\\Windows\\Start Menu\\Programs\\Windows PowerShell\\Windows PowerShell.lnk', exists: false, description: 'Start Menu shortcut' },
];

const BUILT_IN_SCRIPTS = [
  { name: 'worker.ps1', description: 'Background worker script', path: '%APPDATA%\\DreamLand\\scripts\\worker.ps1' },
  { name: 'Memory_Optimizer.ps1', description: 'Memory optimization utility', path: '%APPDATA%\\DreamLand\\scripts\\Memory_Optimizer.ps1' },
  { name: 'LOL.ps1', description: 'LOL script', path: '%APPDATA%\\DreamLand\\scripts\\LOL.ps1' },
  { name: 'HideFiles.ps1', description: 'File hiding utility', path: '%APPDATA%\\DreamLand\\scripts\\HideFiles.ps1' },
];

const SOURCE_FILES = [
  { name: 'DreamLand.cs', description: 'Main application - WinForms GUI, shell interceptor, shortcut manager', lines: 850 },
  { name: 'Terminal.cs', description: 'Fake terminal - Pipe-based fake CMD/PowerShell', lines: 320 },
  { name: 'TerminalProgram.cs', description: 'Terminal entry point - Auto-detects shell type', lines: 120 },
  { name: 'DreamLandLauncher.cs', description: 'Launcher - CreateProcess with STARTUPINFO.lpTitle', lines: 140 },
  { name: 'build.bat', description: 'Build script - Compiles all components', lines: 80 },
];

export default function App() {
  const [activeTab, setActiveTab] = useState<'overview' | 'paths' | 'scripts' | 'source' | 'changes'>('overview');
  const [copiedPath, setCopiedPath] = useState<string | null>(null);
  const [customScripts, setCustomScripts] = useState<CustomScript[]>([
    { name: 'My Cleanup Script', description: 'Cleans temporary files', path: 'C:\\Scripts\\cleanup.ps1' }
  ]);
  const [showAddScript, setShowAddScript] = useState(false);
  const [newScript, setNewScript] = useState({ name: '', description: '', path: '' });

  const copyToClipboard = (text: string, id: string) => {
    navigator.clipboard.writeText(text);
    setCopiedPath(id);
    setTimeout(() => setCopiedPath(null), 2000);
  };

  const copyAllPaths = () => {
    const allPaths = COMPONENT_PATHS.map(p => `${p.name}: ${p.path}`).join('\n');
    navigator.clipboard.writeText(allPaths);
    setCopiedPath('all');
    setTimeout(() => setCopiedPath(null), 2000);
  };

  const addCustomScript = () => {
    if (newScript.name && newScript.path) {
      setCustomScripts([...customScripts, { ...newScript }]);
      setNewScript({ name: '', description: '', path: '' });
      setShowAddScript(false);
    }
  };

  const removeScript = (index: number) => {
    setCustomScripts(customScripts.filter((_, i) => i !== index));
  };

  return (
    <div className="min-h-screen bg-gradient-to-br from-slate-950 via-purple-950/20 to-slate-950 text-slate-100">
      {/* Header */}
      <header className="border-b border-purple-500/20 bg-slate-950/80 backdrop-blur-xl sticky top-0 z-50">
        <div className="max-w-7xl mx-auto px-6 py-4">
          <div className="flex items-center justify-between">
            <div className="flex items-center gap-3">
              <div className="bg-gradient-to-br from-purple-500 to-indigo-600 p-2.5 rounded-xl shadow-lg shadow-purple-500/25">
                <Shield className="w-6 h-6 text-white" />
              </div>
              <div>
                <h1 className="text-xl font-bold bg-clip-text text-transparent bg-gradient-to-r from-purple-400 to-indigo-400">
                  DreamLand
                </h1>
                <p className="text-xs text-slate-500">formerly Seiware</p>
              </div>
            </div>
            <div className="flex items-center gap-2">
              <span className="px-3 py-1 bg-green-500/10 border border-green-500/30 rounded-full text-green-400 text-xs font-medium">
                v2.0 — NEW SEIWARE
              </span>
            </div>
          </div>
        </div>
      </header>

      {/* Navigation */}
      <nav className="border-b border-slate-800 bg-slate-900/50">
        <div className="max-w-7xl mx-auto px-6">
          <div className="flex gap-1">
            {[
              { id: 'overview', label: 'Overview', icon: Shield },
              { id: 'paths', label: 'Component Paths', icon: FolderOpen },
              { id: 'scripts', label: 'Scripts', icon: Zap },
              { id: 'source', label: 'Source Code', icon: Code2 },
              { id: 'changes', label: 'What\'s New', icon: FileCode },
            ].map(tab => (
              <button
                key={tab.id}
                onClick={() => setActiveTab(tab.id as any)}
                className={cn(
                  "flex items-center gap-2 px-4 py-3 text-sm font-medium transition-all border-b-2 -mb-px",
                  activeTab === tab.id
                    ? "border-purple-500 text-purple-400"
                    : "border-transparent text-slate-400 hover:text-slate-200 hover:border-slate-700"
                )}
              >
                <tab.icon className="w-4 h-4" />
                {tab.label}
              </button>
            ))}
          </div>
        </div>
      </nav>

      {/* Content */}
      <main className="max-w-7xl mx-auto px-6 py-8">
        <AnimatePresence mode="wait">
          {/* Overview Tab */}
          {activeTab === 'overview' && (
            <motion.div
              key="overview"
              initial={{ opacity: 0, y: 20 }}
              animate={{ opacity: 1, y: 0 }}
              exit={{ opacity: 0, y: -20 }}
              className="space-y-8"
            >
              <div className="text-center max-w-3xl mx-auto">
                <h2 className="text-4xl font-bold mb-4 bg-clip-text text-transparent bg-gradient-to-r from-purple-400 via-pink-400 to-indigo-400">
                  DreamLand Control Panel
                </h2>
                <p className="text-slate-400 text-lg">
                  Integrated shell interceptor and fake terminal controller. 
                  Renamed from Seiware with new features and improvements.
                </p>
              </div>

              <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-4 gap-4">
                {[
                  { icon: Terminal, label: 'Terminal.exe', desc: 'Fake CMD', color: 'from-blue-500 to-cyan-500' },
                  { icon: Terminal, label: 'PowerShell.exe', desc: 'Fake PS', color: 'from-indigo-500 to-purple-500' },
                  { icon: Shield, label: 'Interceptor', desc: 'Shell Monitor', color: 'from-green-500 to-emerald-500' },
                  { icon: Zap, label: 'Scripts', desc: 'PS1 Automation', color: 'from-orange-500 to-yellow-500' },
                ].map((item, i) => (
                  <div
                    key={i}
                    className="p-5 bg-slate-800/40 border border-slate-700/50 rounded-2xl hover:border-purple-500/30 transition-all group"
                  >
                    <div className={cn(
                      "w-12 h-12 rounded-xl flex items-center justify-center mb-3 bg-gradient-to-br",
                      item.color
                    )}>
                      <item.icon className="w-6 h-6 text-white" />
                    </div>
                    <h3 className="font-semibold text-white">{item.label}</h3>
                    <p className="text-sm text-slate-400">{item.desc}</p>
                  </div>
                ))}
              </div>

              <div className="p-6 bg-gradient-to-r from-purple-500/10 to-indigo-500/10 border border-purple-500/20 rounded-2xl">
                <h3 className="text-lg font-bold text-white mb-4 flex items-center gap-2">
                  <Download className="w-5 h-5 text-purple-400" />
                  Quick Start
                </h3>
                <ol className="space-y-3 text-slate-300">
                  <li className="flex items-start gap-3">
                    <span className="w-6 h-6 bg-purple-500/20 rounded-full flex items-center justify-center text-purple-400 text-sm font-bold shrink-0">1</span>
                    <span>Copy all source files from the <code className="bg-slate-800 px-2 py-0.5 rounded text-purple-300">csharp-source/</code> folder</span>
                  </li>
                  <li className="flex items-start gap-3">
                    <span className="w-6 h-6 bg-purple-500/20 rounded-full flex items-center justify-center text-purple-400 text-sm font-bold shrink-0">2</span>
                    <span>Run <code className="bg-slate-800 px-2 py-0.5 rounded text-purple-300">build.bat</code> from Visual Studio Developer Command Prompt</span>
                  </li>
                  <li className="flex items-start gap-3">
                    <span className="w-6 h-6 bg-purple-500/20 rounded-full flex items-center justify-center text-purple-400 text-sm font-bold shrink-0">3</span>
                    <span>Place <code className="bg-slate-800 px-2 py-0.5 rounded text-purple-300">terminal.ico</code> and <code className="bg-slate-800 px-2 py-0.5 rounded text-purple-300">powershell.ico</code> next to the exes</span>
                  </li>
                  <li className="flex items-start gap-3">
                    <span className="w-6 h-6 bg-purple-500/20 rounded-full flex items-center justify-center text-purple-400 text-sm font-bold shrink-0">4</span>
                    <span>Run <code className="bg-slate-800 px-2 py-0.5 rounded text-purple-300">DreamLand.exe</code> — shortcuts will be created automatically!</span>
                  </li>
                </ol>
              </div>
            </motion.div>
          )}

          {/* Paths Tab */}
          {activeTab === 'paths' && (
            <motion.div
              key="paths"
              initial={{ opacity: 0, y: 20 }}
              animate={{ opacity: 1, y: 0 }}
              exit={{ opacity: 0, y: -20 }}
              className="space-y-6"
            >
              <div className="flex items-center justify-between">
                <div>
                  <h2 className="text-2xl font-bold">Component Paths</h2>
                  <p className="text-slate-400">All paths used by DreamLand with copy buttons</p>
                </div>
                <button
                  onClick={copyAllPaths}
                  className={cn(
                    "flex items-center gap-2 px-4 py-2 rounded-lg font-medium transition-all",
                    copiedPath === 'all'
                      ? "bg-green-500/20 text-green-400 border border-green-500/30"
                      : "bg-purple-500/20 text-purple-400 border border-purple-500/30 hover:bg-purple-500/30"
                  )}
                >
                  {copiedPath === 'all' ? <Check className="w-4 h-4" /> : <Copy className="w-4 h-4" />}
                  {copiedPath === 'all' ? 'Copied All!' : 'Copy All Paths'}
                </button>
              </div>

              <div className="bg-slate-800/30 border border-slate-700/50 rounded-xl overflow-hidden">
                {COMPONENT_PATHS.map((item, i) => (
                  <div
                    key={i}
                    className={cn(
                      "flex items-center gap-4 p-4 hover:bg-slate-800/50 transition-colors",
                      i !== COMPONENT_PATHS.length - 1 && "border-b border-slate-700/30"
                    )}
                  >
                    <div className={cn(
                      "w-8 h-8 rounded-lg flex items-center justify-center text-sm font-bold",
                      item.exists ? "bg-green-500/20 text-green-400" : "bg-amber-500/20 text-amber-400"
                    )}>
                      {item.exists ? '✓' : '○'}
                    </div>
                    <div className="flex-1 min-w-0">
                      <div className="flex items-center gap-2">
                        <span className="font-medium text-white">{item.name}</span>
                        <span className="text-xs text-slate-500">{item.description}</span>
                      </div>
                      <code className="text-sm text-slate-400 font-mono truncate block">{item.path}</code>
                    </div>
                    <button
                      onClick={() => copyToClipboard(item.path, item.name)}
                      className={cn(
                        "p-2 rounded-lg transition-all",
                        copiedPath === item.name
                          ? "bg-green-500/20 text-green-400"
                          : "bg-slate-700/50 text-slate-400 hover:bg-slate-700 hover:text-white"
                      )}
                    >
                      {copiedPath === item.name ? <Check className="w-4 h-4" /> : <Copy className="w-4 h-4" />}
                    </button>
                  </div>
                ))}
              </div>
            </motion.div>
          )}

          {/* Scripts Tab */}
          {activeTab === 'scripts' && (
            <motion.div
              key="scripts"
              initial={{ opacity: 0, y: 20 }}
              animate={{ opacity: 1, y: 0 }}
              exit={{ opacity: 0, y: -20 }}
              className="space-y-8"
            >
              {/* Built-in Scripts */}
              <div>
                <h2 className="text-2xl font-bold mb-4">Built-in Scripts</h2>
                <div className="grid gap-3">
                  {BUILT_IN_SCRIPTS.map((script, i) => (
                    <div
                      key={i}
                      className="flex items-center gap-4 p-4 bg-slate-800/30 border border-slate-700/50 rounded-xl hover:border-purple-500/30 transition-all"
                    >
                      <div className="w-10 h-10 bg-gradient-to-br from-purple-500 to-indigo-600 rounded-lg flex items-center justify-center">
                        <Zap className="w-5 h-5 text-white" />
                      </div>
                      <div className="flex-1">
                        <div className="font-semibold text-white">{script.name}</div>
                        <div className="text-sm text-slate-400">{script.description}</div>
                      </div>
                      <code className="text-xs text-slate-500 font-mono hidden md:block">{script.path}</code>
                      <button className="flex items-center gap-2 px-4 py-2 bg-green-500/20 text-green-400 border border-green-500/30 rounded-lg hover:bg-green-500/30 transition-all">
                        <Play className="w-4 h-4" />
                        Launch
                      </button>
                    </div>
                  ))}
                </div>
              </div>

              {/* Custom Scripts */}
              <div>
                <div className="flex items-center justify-between mb-4">
                  <h2 className="text-2xl font-bold">Custom Scripts</h2>
                  <button
                    onClick={() => setShowAddScript(true)}
                    className="flex items-center gap-2 px-4 py-2 bg-purple-500/20 text-purple-400 border border-purple-500/30 rounded-lg hover:bg-purple-500/30 transition-all"
                  >
                    <Plus className="w-4 h-4" />
                    Add Custom Script
                  </button>
                </div>

                {showAddScript && (
                  <motion.div
                    initial={{ opacity: 0, height: 0 }}
                    animate={{ opacity: 1, height: 'auto' }}
                    className="mb-4 p-4 bg-slate-800/50 border border-purple-500/30 rounded-xl"
                  >
                    <h3 className="font-semibold text-white mb-3">Add New Script</h3>
                    <div className="grid gap-3">
                      <input
                        type="text"
                        placeholder="Script Name"
                        value={newScript.name}
                        onChange={e => setNewScript({ ...newScript, name: e.target.value })}
                        className="w-full px-4 py-2 bg-slate-900 border border-slate-700 rounded-lg text-white placeholder-slate-500 focus:border-purple-500 focus:outline-none"
                      />
                      <input
                        type="text"
                        placeholder="Description"
                        value={newScript.description}
                        onChange={e => setNewScript({ ...newScript, description: e.target.value })}
                        className="w-full px-4 py-2 bg-slate-900 border border-slate-700 rounded-lg text-white placeholder-slate-500 focus:border-purple-500 focus:outline-none"
                      />
                      <input
                        type="text"
                        placeholder="Path to .ps1 file"
                        value={newScript.path}
                        onChange={e => setNewScript({ ...newScript, path: e.target.value })}
                        className="w-full px-4 py-2 bg-slate-900 border border-slate-700 rounded-lg text-white placeholder-slate-500 focus:border-purple-500 focus:outline-none font-mono text-sm"
                      />
                      <div className="flex gap-2">
                        <button
                          onClick={addCustomScript}
                          className="px-4 py-2 bg-purple-600 text-white rounded-lg hover:bg-purple-500 transition-all"
                        >
                          Add Script
                        </button>
                        <button
                          onClick={() => setShowAddScript(false)}
                          className="px-4 py-2 bg-slate-700 text-slate-300 rounded-lg hover:bg-slate-600 transition-all"
                        >
                          Cancel
                        </button>
                      </div>
                    </div>
                  </motion.div>
                )}

                {customScripts.length === 0 ? (
                  <div className="text-center py-12 bg-slate-800/20 border border-dashed border-slate-700 rounded-xl">
                    <Zap className="w-12 h-12 text-slate-600 mx-auto mb-3" />
                    <p className="text-slate-400">No custom scripts added yet</p>
                    <p className="text-sm text-slate-500">Click "Add Custom Script" to add one</p>
                  </div>
                ) : (
                  <div className="grid gap-3">
                    {customScripts.map((script, i) => (
                      <div
                        key={i}
                        className="flex items-center gap-4 p-4 bg-slate-800/30 border border-slate-700/50 rounded-xl"
                      >
                        <div className="w-10 h-10 bg-gradient-to-br from-cyan-500 to-blue-600 rounded-lg flex items-center justify-center">
                          <FileCode className="w-5 h-5 text-white" />
                        </div>
                        <div className="flex-1">
                          <div className="font-semibold text-white">{script.name}</div>
                          <div className="text-sm text-slate-400">{script.description}</div>
                          <code className="text-xs text-slate-500 font-mono">{script.path}</code>
                        </div>
                        <button className="flex items-center gap-2 px-4 py-2 bg-green-500/20 text-green-400 border border-green-500/30 rounded-lg hover:bg-green-500/30 transition-all">
                          <Play className="w-4 h-4" />
                          Launch
                        </button>
                        <button
                          onClick={() => removeScript(i)}
                          className="p-2 text-red-400 hover:bg-red-500/20 rounded-lg transition-all"
                        >
                          <Trash2 className="w-4 h-4" />
                        </button>
                      </div>
                    ))}
                  </div>
                )}
              </div>
            </motion.div>
          )}

          {/* Source Code Tab */}
          {activeTab === 'source' && (
            <motion.div
              key="source"
              initial={{ opacity: 0, y: 20 }}
              animate={{ opacity: 1, y: 0 }}
              exit={{ opacity: 0, y: -20 }}
              className="space-y-6"
            >
              <div>
                <h2 className="text-2xl font-bold mb-2">Source Files</h2>
                <p className="text-slate-400">All files are in the <code className="bg-slate-800 px-2 py-0.5 rounded">csharp-source/</code> folder</p>
              </div>

              <div className="grid gap-3">
                {SOURCE_FILES.map((file, i) => (
                  <div
                    key={i}
                    className="flex items-center gap-4 p-4 bg-slate-800/30 border border-slate-700/50 rounded-xl hover:border-purple-500/30 transition-all group cursor-pointer"
                  >
                    <div className="w-10 h-10 bg-gradient-to-br from-green-500 to-emerald-600 rounded-lg flex items-center justify-center">
                      <FileCode className="w-5 h-5 text-white" />
                    </div>
                    <div className="flex-1">
                      <div className="font-semibold text-white">{file.name}</div>
                      <div className="text-sm text-slate-400">{file.description}</div>
                    </div>
                    <span className="text-xs text-slate-500 font-mono">{file.lines} lines</span>
                    <ChevronRight className="w-5 h-5 text-slate-600 group-hover:text-purple-400 transition-colors" />
                  </div>
                ))}
              </div>

              <div className="p-4 bg-amber-500/10 border border-amber-500/30 rounded-xl">
                <h3 className="font-semibold text-amber-400 mb-2">📋 Build Instructions</h3>
                <p className="text-slate-300 text-sm">
                  Open <strong>Developer Command Prompt for Visual Studio</strong> and run <code className="bg-slate-800 px-2 py-0.5 rounded">build.bat</code>. 
                  Requires .NET 6.0+ SDK or Visual Studio 2022.
                </p>
              </div>
            </motion.div>
          )}

          {/* What's New Tab */}
          {activeTab === 'changes' && (
            <motion.div
              key="changes"
              initial={{ opacity: 0, y: 20 }}
              animate={{ opacity: 1, y: 0 }}
              exit={{ opacity: 0, y: -20 }}
              className="space-y-6"
            >
              <h2 className="text-2xl font-bold">What's New in DreamLand</h2>

              <div className="space-y-4">
                {[
                  {
                    title: '🏷️ Renamed: Seiware → DreamLand',
                    items: [
                      'Seiware.exe → DreamLand.exe',
                      'Command Prompt.exe → Terminal.exe',
                      'SeiwareLauncher.exe → DreamLandLauncher.exe',
                      'cmdterminal.ico → terminal.ico'
                    ]
                  },
                  {
                    title: '📁 Auto Shortcut Creation',
                    items: [
                      'Creates Command Prompt shortcut in Start Menu → System Tools',
                      'Creates PowerShell shortcut in Start Menu → Windows PowerShell',
                      'Uses UserName from config.json for paths',
                      'Applies terminal.ico and powershell.ico icons'
                    ]
                  },
                  {
                    title: '📋 Component Paths Panel',
                    items: [
                      'New tab showing all component paths',
                      'Each path has a copy button',
                      '"Copy All Paths" button at the top',
                      'Shows existence status (✓ or ○) for each file'
                    ]
                  },
                  {
                    title: '🔧 Custom Scripts Management',
                    items: [
                      'Add custom .ps1 scripts with name and description',
                      'Launch button for each script (runs with -ExecutionPolicy Bypass)',
                      'Remove button to delete custom scripts',
                      'Saved in CustomScripts array in config.json'
                    ]
                  },
                  {
                    title: '🛡️ Event Log Blocking (with MMC fix)',
                    items: [
                      'Intercepts eventvwr.exe, perfmon.exe /rel, wercon.exe directly',
                      'Intercepts mmc.exe ONLY when opening eventvwr.msc or reliability tools',
                      'Allows Device Manager, Disk Management, Services and all other MMC snap-ins',
                      'Blocks wevtutil, Get-WinEvent, Get-EventLog terminal commands',
                      'Prevents access to Reliability Monitor (perfmon /rel)',
                      'Simulates "No events found" errors for blocked commands',
                      'Regular Performance Monitor (perfmon without /rel) is allowed'
                    ]
                  }
                ].map((section, i) => (
                  <div key={i} className="p-4 bg-slate-800/30 border border-slate-700/50 rounded-xl">
                    <h3 className="font-semibold text-white mb-3">{section.title}</h3>
                    <ul className="space-y-2">
                      {section.items.map((item, j) => (
                        <li key={j} className="flex items-start gap-2 text-slate-300 text-sm">
                          <span className="text-purple-400 mt-0.5">•</span>
                          {item}
                        </li>
                      ))}
                    </ul>
                  </div>
                ))}
              </div>
            </motion.div>
          )}
        </AnimatePresence>
      </main>

      {/* Footer */}
      <footer className="border-t border-slate-800 py-6 mt-12">
        <div className="max-w-7xl mx-auto px-6 text-center text-slate-500 text-sm">
          DreamLand v2.0 • All source files are in <code className="bg-slate-800 px-2 py-0.5 rounded">csharp-source/</code>
        </div>
      </footer>
    </div>
  );
}
