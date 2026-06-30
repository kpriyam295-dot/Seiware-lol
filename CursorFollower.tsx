import { useEffect, useRef } from 'react';
import { useTheme } from '../ThemeContext';

export default function CursorFollower() {
  const { themeRef } = useTheme();
  // Capture themeRef into a local ref so the effect closure is stable
  const tRef = useRef(themeRef);
  tRef.current = themeRef;

  const dotRef = useRef<HTMLDivElement>(null);
  const ringRef = useRef<HTMLDivElement>(null);
  const glowRef = useRef<HTMLDivElement>(null);
  const pulseCanvasRef = useRef<HTMLCanvasElement>(null);

  const mousePos = useRef({ x: -200, y: -200 });
  const ringPos = useRef({ x: -200, y: -200 });
  const glowPos = useRef({ x: -200, y: -200 });
  const isHovering = useRef(false);
  const isVisible = useRef(false);

  useEffect(() => {
    // Pulse state — fully inside the effect, not in refs that could leak across re-renders
    let pulses: { x: number; y: number; birth: number }[] = [];
    let lastPulseTime = 0;
    let lastClickTime = 0;

    const onMove = (e: MouseEvent) => {
      mousePos.current.x = e.clientX;
      mousePos.current.y = e.clientY;
      isVisible.current = true;
    };

    const onOver = (e: MouseEvent) => {
      if ((e.target as HTMLElement)?.closest?.('a, button, [data-hover], input')) {
        isHovering.current = true;
      }
    };
    const onOut = (e: MouseEvent) => {
      if ((e.target as HTMLElement)?.closest?.('a, button, [data-hover], input')) {
        isHovering.current = false;
      }
    };
    const onLeave = () => { isVisible.current = false; };
    const onEnter = () => { isVisible.current = true; };

    const onClick = (e: MouseEvent) => {
      const now = performance.now();
      if (now - lastClickTime < 250) return; // throttle
      lastClickTime = now;
      // Always clean expired before adding
      pulses = pulses.filter(p => now - p.birth < 1200);
      if (pulses.length < 4) {
        pulses.push({ x: e.clientX, y: e.clientY, birth: now });
      }
    };

    window.addEventListener('mousemove', onMove);
    window.addEventListener('mouseleave', onLeave);
    window.addEventListener('mouseenter', onEnter);
    window.addEventListener('click', onClick);
    document.addEventListener('mouseover', onOver);
    document.addEventListener('mouseout', onOut);

    const pulseCanvas = pulseCanvasRef.current;
    let cw = 0, ch = 0;
    const resizeCanvas = () => {
      if (!pulseCanvas) return;
      cw = window.innerWidth;
      ch = window.innerHeight;
      pulseCanvas.width = cw;
      pulseCanvas.height = ch;
    };
    resizeCanvas();
    window.addEventListener('resize', resizeCanvas);

    let frame: number;
    let running = true;

    const loop = (now: number) => {
      if (!running) return;

      const mx = mousePos.current.x;
      const my = mousePos.current.y;
      const vis = isVisible.current;
      const hover = isHovering.current;
      const t = tRef.current.current; // themeRef.current
      const c = `${t.r}, ${t.g}, ${t.b}`;

      // Dot: instant position
      if (dotRef.current) {
        const s = hover ? 2.2 : 1;
        dotRef.current.style.transform = `translate(${mx - 5}px, ${my - 5}px) scale(${s})`;
        dotRef.current.style.opacity = vis ? '1' : '0';
        dotRef.current.style.backgroundColor = `rgb(${c})`;
        dotRef.current.style.boxShadow = `0 0 12px rgba(${c}, 0.9), 0 0 25px rgba(${c}, 0.4)`;
      }

      // Ring: lerp every frame → always catches up
      ringPos.current.x += (mx - ringPos.current.x) * 0.18;
      ringPos.current.y += (my - ringPos.current.y) * 0.18;
      if (Math.abs(mx - ringPos.current.x) < 0.5) ringPos.current.x = mx;
      if (Math.abs(my - ringPos.current.y) < 0.5) ringPos.current.y = my;

      if (ringRef.current) {
        const s = hover ? 1.8 : 1;
        ringRef.current.style.transform = `translate(${ringPos.current.x - 22}px, ${ringPos.current.y - 22}px) scale(${s})`;
        ringRef.current.style.opacity = vis ? '1' : '0';
        ringRef.current.style.borderColor = `rgba(${c}, 0.6)`;
      }

      // Glow: more lag
      glowPos.current.x += (mx - glowPos.current.x) * 0.1;
      glowPos.current.y += (my - glowPos.current.y) * 0.1;
      if (Math.abs(mx - glowPos.current.x) < 0.8) glowPos.current.x = mx;
      if (Math.abs(my - glowPos.current.y) < 0.8) glowPos.current.y = my;

      if (glowRef.current) {
        glowRef.current.style.transform = `translate(${glowPos.current.x - 80}px, ${glowPos.current.y - 80}px)`;
        glowRef.current.style.opacity = vis ? '1' : '0';
        glowRef.current.style.background = `radial-gradient(circle, rgba(${c}, 0.14) 0%, rgba(${c}, 0.04) 45%, transparent 70%)`;
      }

      // Auto-pulses every 2.5s
      if (vis && now - lastPulseTime > 2500) {
        pulses = pulses.filter(p => now - p.birth < 1200);
        if (pulses.length < 4) {
          pulses.push({ x: mx, y: my, birth: now });
        }
        lastPulseTime = now;
      }

      // Draw pulse rings
      if (pulseCanvas) {
        const pctx = pulseCanvas.getContext('2d');
        if (pctx) {
          pctx.clearRect(0, 0, cw, ch);
          const alive: typeof pulses = [];
          for (const p of pulses) {
            const age = (now - p.birth) / 1000;
            if (age > 1.2) continue;
            alive.push(p);
            const radius = age * 55;
            const alpha = (1 - age / 1.2) * 0.3;
            pctx.beginPath();
            pctx.arc(p.x, p.y, radius, 0, Math.PI * 2);
            pctx.strokeStyle = `rgba(${c}, ${alpha})`;
            pctx.lineWidth = 1.2 * (1 - age / 1.2);
            pctx.stroke();
          }
          pulses = alive;
        }
      }

      frame = requestAnimationFrame(loop);
    };

    frame = requestAnimationFrame(loop);

    return () => {
      running = false;
      cancelAnimationFrame(frame);
      window.removeEventListener('mousemove', onMove);
      window.removeEventListener('mouseleave', onLeave);
      window.removeEventListener('mouseenter', onEnter);
      window.removeEventListener('click', onClick);
      window.removeEventListener('resize', resizeCanvas);
      document.removeEventListener('mouseover', onOver);
      document.removeEventListener('mouseout', onOut);
    };
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []); // Empty deps — reads theme from ref, never re-mounts

  return (
    <div className="pointer-events-none fixed inset-0 z-[9999] select-none">
      <canvas ref={pulseCanvasRef} className="absolute inset-0" style={{ width: '100%', height: '100%' }} />
      <div ref={glowRef} className="absolute w-[160px] h-[160px] rounded-full" style={{ filter: 'blur(8px)', willChange: 'transform' }} />
      <div ref={ringRef} className="absolute w-[44px] h-[44px] rounded-full border-[1.5px]" style={{ mixBlendMode: 'screen', willChange: 'transform' }} />
      <div ref={dotRef} className="absolute w-[10px] h-[10px] rounded-full" style={{ willChange: 'transform' }} />
    </div>
  );
}
