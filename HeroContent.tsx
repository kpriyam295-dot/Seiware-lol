import { useEffect, useState, useMemo } from 'react';
import { useTheme } from '../ThemeContext';
import { TabName } from './TabPanel';

interface HeroContentProps {
  onTabOpen: (tab: TabName) => void;
}

const glitchText = (text: string) => {
  const chars = '!@#$%^&*()_+-=[]{}|;:,.<>?/~`';
  return text.split('').map((ch) => (Math.random() > 0.7 ? chars[Math.floor(Math.random() * chars.length)] : ch)).join('');
};

const DEFAULT_FACTS = [
  'The system sees everything. The grid never sleeps.',
  'Every node is a heartbeat. Every pulse is a signal.',
  'Click anywhere. Watch the shockwave ripple through reality.',
  'You are the cursor. The grid bends to your will.',
  'Built different. Engineered for those who push boundaries.',
  'The future doesn\'t wait. Neither should you.',
  'Chaos is just order waiting to be discovered.',
  'Not all who wander the grid are lost.',
  'Precision is not perfection — it\'s intention.',
  'Some see a grid. Others see infinite possibility.',
  'Every frame is a conversation between code and canvas.',
  'Break the pattern. Rewrite the signal.',
];

export default function HeroContent({ onTabOpen }: HeroContentProps) {
  const { theme } = useTheme();
  const [displayedText, setDisplayedText] = useState('');
  const [isGlitching, setIsGlitching] = useState(false);
  const [factIndex, setFactIndex] = useState(0);
  const [factFade, setFactFade] = useState(true);

  const c = `${theme.r}, ${theme.g}, ${theme.b}`;

  // Determine which fact text to show
  const factText = theme.funFact
    ? theme.funFact
    : DEFAULT_FACTS[factIndex];

  // Typewriter on title change
  useEffect(() => {
    setDisplayedText('');
    let i = 0;
    const interval = setInterval(() => {
      if (i <= theme.title.length) {
        setDisplayedText(theme.title.slice(0, i));
        i++;
      } else {
        clearInterval(interval);
      }
    }, 100);
    return () => clearInterval(interval);
  }, [theme.title]);

  // Glitch
  useEffect(() => {
    const interval = setInterval(() => {
      setIsGlitching(true);
      setTimeout(() => setIsGlitching(false), 150);
    }, 4000);
    return () => clearInterval(interval);
  }, []);

  // Auto-rotate default facts (only when no custom fact is set)
  useEffect(() => {
    if (theme.funFact) return; // custom fact set, don't rotate
    const interval = setInterval(() => {
      setFactFade(false);
      setTimeout(() => {
        setFactIndex((prev) => (prev + 1) % DEFAULT_FACTS.length);
        setFactFade(true);
      }, 400);
    }, 3000);
    return () => clearInterval(interval);
  }, [theme.funFact]);

  // Dynamic font size — scales smoothly with both title length and viewport
  const titleFontSize = useMemo(() => {
    const len = theme.title.length;
    if (len <= 3) return 'clamp(4rem, 15vw, 11rem)';
    if (len <= 5) return 'clamp(3.5rem, 13vw, 9rem)';
    if (len <= 7) return 'clamp(2.8rem, 11vw, 7.5rem)';
    if (len <= 9) return 'clamp(2.2rem, 9vw, 6rem)';
    if (len <= 12) return 'clamp(1.8rem, 7vw, 4.5rem)';
    if (len <= 16) return 'clamp(1.4rem, 5.5vw, 3.5rem)';
    if (len <= 20) return 'clamp(1.1rem, 4.5vw, 2.8rem)';
    return 'clamp(0.9rem, 3.5vw, 2.2rem)';
  }, [theme.title]);

  const stats = [
    { label: 'NODES', value: '2,847' },
    { label: 'ACTIVE', value: '1,293' },
    { label: 'LATENCY', value: '12ms' },
    { label: 'UPTIME', value: '99.97%' },
  ];

  return (
    <>
      {/* Blur overlay */}
      {theme.blurEnabled && (
        <div className="fixed inset-0 z-[1] backdrop-blur-sm pointer-events-none" />
      )}

      <div className="fixed inset-0 z-10 flex flex-col pointer-events-none">
        {/* Top bar */}
        <div className="flex items-center justify-between px-4 sm:px-6 py-3 sm:py-4 pointer-events-auto">
          <div className="flex items-center gap-2 font-mono text-[10px] sm:text-xs" style={{ color: `rgba(${c}, 0.5)` }}>
            <div className="w-1.5 h-1.5 sm:w-2 sm:h-2 rounded-full animate-pulse" style={{ backgroundColor: `rgb(${c})` }} />
            System Online
          </div>
          <div className="hidden sm:flex gap-4 md:gap-6">
            {[
              { label: 'Scripts', tab: 'scripts' as TabName },
              { label: 'Terminal', tab: 'terminal' as TabName },
              { label: 'Config', tab: 'config' as TabName },
            ].map((item) => (
              <button
                key={item.label}
                data-hover
                onClick={() => onTabOpen(item.tab)}
                className="font-mono text-[10px] sm:text-xs tracking-wider uppercase transition-colors pointer-events-auto"
                style={{ color: `rgba(${c}, 0.3)` }}
                onMouseEnter={(e) => { e.currentTarget.style.color = `rgba(${c}, 0.8)`; }}
                onMouseLeave={(e) => { e.currentTarget.style.color = `rgba(${c}, 0.3)`; }}
              >
                {item.label}
              </button>
            ))}
          </div>
          <span className="font-mono text-[10px] sm:text-xs" style={{ color: `rgba(${c}, 0.2)` }}>v3.7.1</span>
        </div>

        {/* ── Hero center block ── */}
        <div className="flex-1 flex flex-col items-center justify-center px-4 min-h-0">

          {/* Decorative line + diamond — centered */}
          <div className="flex items-center gap-2 sm:gap-3 mb-3 sm:mb-4">
            <div className="w-10 sm:w-16 h-[1px]" style={{ background: `linear-gradient(to right, transparent, rgba(${c}, 0.3))` }} />
            <div className="w-1.5 h-1.5 sm:w-2 sm:h-2 rotate-45" style={{ border: `1px solid rgba(${c}, 0.4)` }} />
            <div className="w-10 sm:w-16 h-[1px]" style={{ background: `linear-gradient(to left, transparent, rgba(${c}, 0.3))` }} />
          </div>

          {/* Subtitle — centered */}
          <div className="font-mono text-[8px] sm:text-[10px] tracking-[0.3em] sm:tracking-[0.5em] uppercase mb-4 sm:mb-6" style={{ color: `rgba(${c}, 0.35)` }}>
            Interactive Grid Interface
          </div>

          {/* Title — centered, hollow outline */}
          <div className="relative mb-4 sm:mb-6 max-w-[90vw] text-center overflow-hidden" style={{ fontSize: titleFontSize, lineHeight: 1 }}>
            <span
              className="font-black tracking-wider font-mono"
              style={{
                color: 'transparent',
                WebkitTextStroke: `1.5px rgb(${c})`,
                textShadow: `0 0 40px rgba(${c}, 0.25), 0 0 80px rgba(${c}, 0.1)`,
              }}
            >
              {isGlitching ? glitchText(displayedText) : displayedText}
              <span className="animate-pulse" style={{ color: 'transparent', WebkitTextStroke: `1.5px rgba(${c}, 0.5)` }}>_</span>
            </span>
            {isGlitching && (
              <>
                <span
                  className="absolute inset-0 font-black tracking-wider font-mono"
                  style={{
                    color: 'transparent',
                    WebkitTextStroke: `1.5px rgba(${c}, 0.3)`,
                    clipPath: 'inset(20% 0 50% 0)',
                    transform: 'translateX(3px)',
                    fontSize: titleFontSize,
                  }}
                >
                  {glitchText(theme.title)}_
                </span>
                <span
                  className="absolute inset-0 font-black tracking-wider font-mono"
                  style={{
                    color: 'transparent',
                    WebkitTextStroke: `1.5px rgba(${c}, 0.2)`,
                    clipPath: 'inset(60% 0 10% 0)',
                    transform: 'translateX(-2px)',
                    fontSize: titleFontSize,
                  }}
                >
                  {glitchText(theme.title)}_
                </span>
              </>
            )}
          </div>

          {/* Fun fact — centered, fixed height */}
          <div className="h-6 sm:h-8 flex items-center justify-center mb-6 sm:mb-8 px-4">
            <p
              className="font-mono text-[10px] sm:text-xs text-center max-w-[80vw] sm:max-w-md transition-opacity duration-400"
              style={{ color: `rgba(${c}, 0.3)`, opacity: factFade ? 1 : 0 }}
            >
              {factText}
            </p>
          </div>

          {/* CTA Buttons — centered */}
          <div className="flex gap-3 sm:gap-4 pointer-events-auto">
            <div
              className="px-5 sm:px-8 py-2.5 sm:py-3 font-mono text-[10px] sm:text-xs tracking-widest uppercase border"
              style={{
                borderColor: `rgba(${c}, 0.25)`,
                color: `rgba(${c}, 0.5)`,
              }}
            >
              Made by Ang3l
            </div>
            <button
              data-hover
              onClick={() => onTabOpen('scripts')}
              className="px-5 sm:px-8 py-2.5 sm:py-3 font-mono text-[10px] sm:text-xs tracking-widest uppercase border transition-all duration-300"
              style={{
                borderColor: `rgba(${c}, 0.4)`,
                color: `rgb(${c})`,
                backgroundColor: `rgba(${c}, 0.05)`,
              }}
              onMouseEnter={(e) => { e.currentTarget.style.backgroundColor = `rgba(${c}, 0.15)`; e.currentTarget.style.borderColor = `rgba(${c}, 0.8)`; }}
              onMouseLeave={(e) => { e.currentTarget.style.backgroundColor = `rgba(${c}, 0.05)`; e.currentTarget.style.borderColor = `rgba(${c}, 0.4)`; }}
            >
              Launch
            </button>
          </div>
        </div>

        {/* Stats bar */}
        <div className="pb-4 sm:pb-6 px-4 sm:px-6">
          <div className="flex justify-center gap-4 sm:gap-8 flex-wrap">
            {stats.map((stat, i) => (
              <div key={stat.label} className="flex items-center gap-4 sm:gap-8">
                <div className="text-center">
                  <div className="font-mono text-xs sm:text-sm font-bold" style={{ color: `rgba(${c}, 0.6)` }}>{stat.value}</div>
                  <div className="font-mono text-[7px] sm:text-[9px] tracking-[0.2em] sm:tracking-[0.3em] uppercase" style={{ color: `rgba(${c}, 0.2)` }}>{stat.label}</div>
                </div>
                {i < stats.length - 1 && <div className="hidden sm:block w-[1px] h-6" style={{ backgroundColor: `rgba(${c}, 0.1)` }} />}
              </div>
            ))}
          </div>
        </div>

        {/* Side decorations — hidden on small viewports */}
        <div className="hidden md:flex fixed left-6 top-1/2 -translate-y-1/2 flex-col gap-2 pointer-events-auto">
          {Array.from({ length: 5 }).map((_, i) => (
            <div
              key={i}
              className="w-1 h-6 transition-all duration-300"
              style={{ backgroundColor: `rgba(${c}, 0.15)` }}
              onMouseEnter={(e) => { e.currentTarget.style.backgroundColor = `rgba(${c}, 0.5)`; }}
              onMouseLeave={(e) => { e.currentTarget.style.backgroundColor = `rgba(${c}, 0.15)`; }}
            />
          ))}
          <div className="font-mono text-[8px] tracking-widest mt-2" style={{ color: `rgba(${c}, 0.15)`, writingMode: 'vertical-rl' }}>DL_v3</div>
        </div>

        <div
          className="hidden sm:block fixed bottom-4 sm:bottom-6 left-4 sm:left-6 font-mono text-[7px] sm:text-[8px] tracking-[0.2em] sm:tracking-[0.3em] uppercase"
          style={{ color: `rgba(${c}, 0.12)` }}
        >
          GRID_INTERFACE_v3
        </div>
      </div>
    </>
  );
}
