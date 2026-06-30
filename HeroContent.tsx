import { useEffect, useState, useMemo } from 'react';
import { useTheme } from '../ThemeContext';

const glitchText = (text: string) => {
  const chars = '!@#$%^&*()_+-=[]{}|;:,.<>?/~`';
  return text.split('').map((ch) => (Math.random() > 0.7 ? chars[Math.floor(Math.random() * chars.length)] : ch)).join('');
};

const DEFAULT_FACTS = [
  'The grid has over 1,500 interactive vertices reacting in real-time.',
  'Each vertex runs its own spring physics simulation every frame.',
  'Click ripples propagate outward at 320 pixels per second.',
  'The cloth simulation uses neighbor-coupled springs for realism.',
  'Your cursor creates a force field with a 220px radius.',
  'The grid refreshes 60 times per second at full speed.',
  'Every intersection has its own velocity and height value.',
  'The perspective projection simulates a 900mm focal length.',
  'A scan line sweeps the entire grid every 5 seconds.',
  'The ambient wave uses sinusoidal functions for organic motion.',
];

export default function HeroContent() {
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

  // Dynamic font size
  const titleFontSize = useMemo(() => {
    const len = theme.title.length;
    if (len <= 4) return 'clamp(5rem, 14vw, 10rem)';
    if (len <= 6) return 'clamp(4rem, 12vw, 9rem)';
    if (len <= 8) return 'clamp(3rem, 10vw, 7rem)';
    if (len <= 12) return 'clamp(2.2rem, 7vw, 5rem)';
    return 'clamp(1.5rem, 5vw, 3.5rem)';
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
        <div className="fixed inset-0 z-[5] pointer-events-none" style={{ backdropFilter: 'blur(6px)', WebkitBackdropFilter: 'blur(6px)' }} />
      )}

      <div className="relative z-10 flex flex-col items-center justify-center min-h-screen px-6 select-none">
        {/* Top bar */}
        <div className="fixed top-0 left-0 right-0 z-30 flex items-center justify-between px-8 py-5 border-b backdrop-blur-sm bg-black/30" style={{ borderColor: `rgba(${c}, 0.1)` }}>
          <div className="flex items-center gap-3">
            <div className="w-3 h-3 rounded-full animate-pulse" style={{ backgroundColor: `rgb(${c})`, boxShadow: `0 0 10px rgba(${c}, 0.6)` }} />
            <span className="text-xs font-mono tracking-[0.3em] uppercase" style={{ color: `rgba(${c}, 0.7)` }}>System Online</span>
          </div>
          <div className="hidden md:flex items-center gap-8">
            {['Dashboard', 'Network', 'Nodes', 'Terminal'].map((item) => (
              <button
                key={item}
                data-hover
                className="text-xs font-mono tracking-[0.2em] uppercase transition-colors duration-300 relative group"
                style={{ color: `rgba(${c}, 0.35)` }}
                onMouseEnter={(e) => { e.currentTarget.style.color = `rgba(${c}, 0.7)`; }}
                onMouseLeave={(e) => { e.currentTarget.style.color = `rgba(${c}, 0.35)`; }}
              >
                {item}
                <span className="absolute -bottom-1 left-0 w-0 h-[1px] group-hover:w-full transition-all duration-300" style={{ backgroundColor: `rgb(${c})` }} />
              </button>
            ))}
          </div>
          <div className="text-xs font-mono" style={{ color: `rgba(${c}, 0.25)` }}>v3.7.1</div>
        </div>

        {/* ── Hero center block ── */}
        <div className="flex flex-col items-center text-center w-full max-w-2xl mx-auto animate-fade-in-up" style={{ animationDelay: '0.3s', animationFillMode: 'both' }}>

          {/* Decorative line + diamond — centered */}
          <div className="flex items-center justify-center gap-4 mb-6 w-full">
            <div className="w-16 h-[1px]" style={{ background: `linear-gradient(to right, transparent, rgba(${c}, 0.5))` }} />
            <div className="w-2 h-2 rotate-45 border flex-shrink-0" style={{ borderColor: `rgba(${c}, 0.5)` }} />
            <div className="w-16 h-[1px]" style={{ background: `linear-gradient(to left, transparent, rgba(${c}, 0.5))` }} />
          </div>

          {/* Subtitle — centered */}
          <p className="text-xs font-mono tracking-[0.5em] uppercase mb-6 w-full text-center" style={{ color: `rgba(${c}, 0.45)` }}>
            Interactive Grid Interface
          </p>

          {/* Title — centered */}
          <div className="relative mb-6 w-full flex justify-center">
            <h1 className="relative inline-block text-center">
              <span
                className="font-black tracking-tighter leading-none block"
                style={{
                  fontSize: titleFontSize,
                  color: 'transparent',
                  WebkitTextStroke: `2px rgba(${c}, 0.8)`,
                  textShadow: `0 0 40px rgba(${c}, 0.3)`,
                  filter: isGlitching ? 'hue-rotate(20deg)' : 'none',
                }}
              >
                {isGlitching ? glitchText(displayedText) : displayedText}
                <span className="animate-pulse" style={{ color: `rgb(${c})` }}>_</span>
              </span>

              {isGlitching && (
                <>
                  <span
                    className="absolute inset-0 font-black tracking-tighter leading-none flex items-center justify-center"
                    style={{ fontSize: titleFontSize, color: `rgba(${c}, 0.3)`, clipPath: 'inset(20% 0 60% 0)', transform: 'translateX(-4px)' }}
                  >
                    {glitchText(theme.title)}_
                  </span>
                  <span
                    className="absolute inset-0 font-black tracking-tighter leading-none flex items-center justify-center"
                    style={{ fontSize: titleFontSize, color: 'rgba(0, 220, 220, 0.15)', clipPath: 'inset(60% 0 10% 0)', transform: 'translateX(4px)' }}
                  >
                    {glitchText(theme.title)}_
                  </span>
                </>
              )}
            </h1>
          </div>

          {/* Fun fact — centered, fixed height */}
          <div className="h-14 flex items-center justify-center w-full mb-6">
            <p
              className="text-sm md:text-base font-mono leading-relaxed text-center max-w-lg transition-opacity duration-400"
              style={{
                color: `rgba(${c}, 0.4)`,
                opacity: theme.funFact ? 1 : (factFade ? 1 : 0),
              }}
            >
              {factText}
            </p>
          </div>

          {/* CTA Buttons — centered */}
          <div className="flex flex-col sm:flex-row items-center justify-center gap-4">
            <button
              data-hover
              className="group relative px-8 py-3 border font-mono text-sm tracking-[0.2em] uppercase overflow-hidden transition-all duration-500"
              style={{ borderColor: `rgba(${c}, 0.4)`, color: `rgba(${c}, 0.7)` }}
              onMouseEnter={(e) => { e.currentTarget.style.borderColor = `rgba(${c}, 0.7)`; e.currentTarget.style.color = '#fff'; e.currentTarget.style.boxShadow = `0 0 30px rgba(${c}, 0.3)`; }}
              onMouseLeave={(e) => { e.currentTarget.style.borderColor = `rgba(${c}, 0.4)`; e.currentTarget.style.color = `rgba(${c}, 0.7)`; e.currentTarget.style.boxShadow = 'none'; }}
            >
              <span className="relative z-10">Enter System</span>
              <div className="absolute inset-0 opacity-0 group-hover:opacity-100 transition-opacity duration-500" style={{ backgroundColor: `rgba(${c}, 0.12)` }} />
              <div className="absolute inset-0 opacity-0 group-hover:opacity-100 transition-opacity duration-500">
                <div className="absolute top-0 left-0 w-2 h-2 border-t border-l" style={{ borderColor: `rgba(${c}, 0.6)` }} />
                <div className="absolute top-0 right-0 w-2 h-2 border-t border-r" style={{ borderColor: `rgba(${c}, 0.6)` }} />
                <div className="absolute bottom-0 left-0 w-2 h-2 border-b border-l" style={{ borderColor: `rgba(${c}, 0.6)` }} />
                <div className="absolute bottom-0 right-0 w-2 h-2 border-b border-r" style={{ borderColor: `rgba(${c}, 0.6)` }} />
              </div>
            </button>
            <button
              data-hover
              className="px-8 py-3 font-mono text-sm tracking-[0.2em] uppercase transition-colors duration-300"
              style={{ color: `rgba(${c}, 0.35)` }}
              onMouseEnter={(e) => { e.currentTarget.style.color = `rgba(${c}, 0.7)`; }}
              onMouseLeave={(e) => { e.currentTarget.style.color = `rgba(${c}, 0.35)`; }}
            >
              Documentation →
            </button>
          </div>
        </div>

        {/* Stats bar */}
        <div className="fixed bottom-0 left-0 right-0 z-30 border-t backdrop-blur-sm bg-black/30 animate-fade-in-up" style={{ borderColor: `rgba(${c}, 0.1)`, animationDelay: '1s', animationFillMode: 'both' }}>
          <div className="flex items-center justify-between px-8 py-4 max-w-6xl mx-auto">
            {stats.map((stat, i) => (
              <div key={stat.label} className="flex items-center gap-6">
                <div className="text-center">
                  <div className="text-lg md:text-xl font-mono font-bold" style={{ color: `rgba(${c}, 0.75)` }}>{stat.value}</div>
                  <div className="text-[10px] font-mono tracking-[0.3em] uppercase" style={{ color: `rgba(${c}, 0.25)` }}>{stat.label}</div>
                </div>
                {i < stats.length - 1 && <div className="hidden md:block w-[1px] h-8" style={{ backgroundColor: `rgba(${c}, 0.1)` }} />}
              </div>
            ))}
          </div>
        </div>

        {/* Side decorations */}
        <div className="fixed left-6 top-1/2 -translate-y-1/2 z-20 hidden lg:flex flex-col items-center gap-4">
          {Array.from({ length: 5 }).map((_, i) => (
            <div key={i} className="w-[3px] h-8 transition-all duration-500" style={{ backgroundColor: `rgba(${c}, 0.15)` }} data-hover
              onMouseEnter={(e) => { e.currentTarget.style.backgroundColor = `rgba(${c}, 0.5)`; }}
              onMouseLeave={(e) => { e.currentTarget.style.backgroundColor = `rgba(${c}, 0.15)`; }}
            />
          ))}
          <div className="w-[1px] h-16" style={{ background: `linear-gradient(to bottom, rgba(${c}, 0.25), transparent)` }} />
        </div>

        <div className="fixed right-6 top-1/2 -translate-y-1/2 z-20 hidden lg:flex flex-col items-center gap-2">
          <div className="text-[10px] font-mono tracking-widest" style={{ writingMode: 'vertical-rl', color: `rgba(${c}, 0.15)` }}>GRID_INTERFACE_v3</div>
        </div>
      </div>
    </>
  );
}
