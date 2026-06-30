import { useEffect, useRef } from 'react';
import { useTheme } from '../ThemeContext';

interface Vertex {
  baseX: number;
  baseY: number;
  z: number;
  vz: number;
  projX: number;
  projY: number;
}

const CELL = 32;
const HOVER_RADIUS = 220;
const HOVER_STRENGTH = 70;
const CLICK_STRENGTH = 100;
const SPRING = 0.016;
const DAMPING = 0.92;
const NEIGHBOR_TRANSFER = 0.22;
const PERSPECTIVE_D = 900;
const SCAN_PERIOD = 5000;
const SCAN_GLOW_WIDTH = 120;
const CENTER_DIM_W = 500;
const CENTER_DIM_H = 280;

export default function InteractiveGrid() {
  const { themeRef } = useTheme();
  const tRef = useRef(themeRef);
  tRef.current = themeRef;

  const canvasRef = useRef<HTMLCanvasElement>(null);
  const mouseRef = useRef({ x: -9999, y: -9999 });
  const verticesRef = useRef<Vertex[][]>([]);
  const dimsRef = useRef({ cols: 0, rows: 0 });

  useEffect(() => {
    const canvas = canvasRef.current;
    if (!canvas) return;
    const ctx = canvas.getContext('2d');
    if (!ctx) return;

    // Click ripples — managed entirely inside the effect
    let clicks: { x: number; y: number; time: number }[] = [];
    let lastClickTime = 0;
    let running = true;

    let dpr = window.devicePixelRatio || 1;

    const buildGrid = () => {
      dpr = window.devicePixelRatio || 1;
      const w = window.innerWidth;
      const h = window.innerHeight;
      canvas.width = w * dpr;
      canvas.height = h * dpr;
      canvas.style.width = w + 'px';
      canvas.style.height = h + 'px';
      ctx.setTransform(dpr, 0, 0, dpr, 0, 0);

      const cols = Math.ceil(w / CELL) + 3;
      const rows = Math.ceil(h / CELL) + 3;
      dimsRef.current = { cols, rows };

      const grid: Vertex[][] = [];
      for (let r = 0; r < rows; r++) {
        const row: Vertex[] = [];
        for (let c = 0; c < cols; c++) {
          row.push({ baseX: c * CELL - CELL, baseY: r * CELL - CELL, z: 0, vz: 0, projX: 0, projY: 0 });
        }
        grid.push(row);
      }
      verticesRef.current = grid;
    };

    buildGrid();
    window.addEventListener('resize', buildGrid);

    const onMove = (e: MouseEvent) => { mouseRef.current = { x: e.clientX, y: e.clientY }; };

    const onClick = (e: MouseEvent) => {
      const now = performance.now();
      if (now - lastClickTime < 250) return;
      lastClickTime = now;
      clicks = clicks.filter(c => now - c.time < 4000);
      if (clicks.length < 5) {
        clicks.push({ x: e.clientX, y: e.clientY, time: now });
      }
    };

    window.addEventListener('mousemove', onMove);
    window.addEventListener('click', onClick);

    const project = (v: Vertex, halfW: number) => {
      const scale = PERSPECTIVE_D / Math.max(1, PERSPECTIVE_D - v.z);
      v.projX = v.baseX + (v.baseX - halfW) * (scale - 1) * 0.15;
      v.projY = v.baseY - v.z * 0.65;
    };

    const smoothstep = (t: number) => t * t * (3 - 2 * t);

    const centerDim = (px: number, py: number, cx: number, cy: number) => {
      const dx = Math.abs(px - cx) / (CENTER_DIM_W / 2);
      const dy = Math.abs(py - cy) / (CENTER_DIM_H / 2);
      const d = Math.max(dx, dy);
      if (d >= 1) return 1;
      return 0.2 + 0.8 * smoothstep(d);
    };

    let frame: number;
    const loop = (time: number) => {
      if (!running) return;
      const grid = verticesRef.current;
      const { cols, rows } = dimsRef.current;
      if (cols === 0 || rows === 0) { frame = requestAnimationFrame(loop); return; }
      const w = window.innerWidth;
      const h = window.innerHeight;
      const mx = mouseRef.current.x;
      const my = mouseRef.current.y;
      const screenCx = w / 2;
      const screenCy = h / 2;

      const t = tRef.current.current;
      const cr = t.r, cg = t.g, cb = t.b;

      const scanProgress = (time % SCAN_PERIOD) / SCAN_PERIOD;
      const scanY = -SCAN_GLOW_WIDTH + (h + SCAN_GLOW_WIDTH * 2) * scanProgress;

      // ════════════════ PHYSICS ════════════════
      for (let r = 0; r < rows; r++) {
        for (let c = 0; c < cols; c++) {
          const v = grid[r][c];
          const dx = mx - v.baseX;
          const dy = my - v.baseY;
          const dist = Math.sqrt(dx * dx + dy * dy);
          if (dist < HOVER_RADIUS) {
            const tt = 1 - dist / HOVER_RADIUS;
            const force = smoothstep(smoothstep(tt));
            v.vz += (force * HOVER_STRENGTH - v.z) * 0.06;
          }

          for (const click of clicks) {
            const cdx = click.x - v.baseX;
            const cdy = click.y - v.baseY;
            const cDist = Math.sqrt(cdx * cdx + cdy * cdy);
            const elapsed = (time - click.time) / 1000;
            const waveRadius = elapsed * 320;
            const waveDist = Math.abs(cDist - waveRadius);
            if (waveDist < 160) {
              const envelope = Math.sin((1 - waveDist / 160) * Math.PI);
              const decay = Math.max(0, 1 - elapsed * 0.45);
              v.vz += (envelope * CLICK_STRENGTH * decay - v.z) * 0.025;
            }
          }

          v.vz += -v.z * SPRING;
          v.vz *= DAMPING;
          v.z += v.vz;
          const ambient = Math.sin(time * 0.0006 + c * 0.35 + r * 0.35) * 2;
          v.z += (ambient - v.z) * 0.002;
        }
      }

      for (let pass = 0; pass < 2; pass++) {
        for (let r = 1; r < rows - 1; r++) {
          for (let c = 1; c < cols - 1; c++) {
            const v = grid[r][c];
            const avg = (grid[r - 1][c].z + grid[r + 1][c].z + grid[r][c - 1].z + grid[r][c + 1].z) / 4;
            v.vz += (avg - v.z) * NEIGHBOR_TRANSFER;
          }
        }
      }

      for (let r = 0; r < rows; r++) {
        for (let c = 0; c < cols; c++) {
          project(grid[r][c], screenCx);
        }
      }

      clicks = clicks.filter(cl => time - cl.time < 5000);

      // ════════════════ RENDER ════════════════
      ctx.clearRect(0, 0, w, h);

      // Scan line bar
      const scanGrad = ctx.createLinearGradient(0, scanY - SCAN_GLOW_WIDTH, 0, scanY + SCAN_GLOW_WIDTH);
      scanGrad.addColorStop(0, `rgba(${cr},${cg},${cb},0)`);
      scanGrad.addColorStop(0.3, `rgba(${cr},${cg},${cb},0.04)`);
      scanGrad.addColorStop(0.5, `rgba(${cr},${cg},${cb},0.07)`);
      scanGrad.addColorStop(0.7, `rgba(${cr},${cg},${cb},0.04)`);
      scanGrad.addColorStop(1, `rgba(${cr},${cg},${cb},0)`);
      ctx.fillStyle = scanGrad;
      ctx.fillRect(0, scanY - SCAN_GLOW_WIDTH, w, SCAN_GLOW_WIDTH * 2);
      ctx.fillStyle = `rgba(${cr},${cg},${cb},0.12)`;
      ctx.fillRect(0, scanY - 1, w, 2);

      const scanBoost = (py: number) => {
        const d = Math.abs(py - scanY);
        if (d > SCAN_GLOW_WIDTH) return 0;
        return smoothstep(1 - d / SCAN_GLOW_WIDTH);
      };

      // Filled quads
      for (let r = 0; r < rows - 1; r++) {
        for (let c = 0; c < cols - 1; c++) {
          const tl = grid[r][c], tr = grid[r][c + 1], bl = grid[r + 1][c], br = grid[r + 1][c + 1];
          const avgZ = (tl.z + tr.z + bl.z + br.z) / 4;
          const intensity = Math.min(1, Math.max(0, avgZ / 40));
          if (intensity > 0.005) {
            const qcx = (tl.projX + br.projX) / 2;
            const qcy = (tl.projY + br.projY) / 2;
            const dim = centerDim(qcx, qcy, screenCx, screenCy);
            ctx.beginPath();
            ctx.moveTo(tl.projX, tl.projY);
            ctx.lineTo(tr.projX, tr.projY);
            ctx.lineTo(br.projX, br.projY);
            ctx.lineTo(bl.projX, bl.projY);
            ctx.closePath();
            ctx.fillStyle = `rgba(${cr},${cg},${cb},${intensity * 0.15 * dim})`;
            ctx.fill();
          }
        }
      }

      // Horizontal grid lines
      for (let r = 0; r < rows; r++) {
        for (let c = 0; c < cols - 1; c++) {
          const v0 = grid[r][c], v1 = grid[r][c + 1];
          const segZ = (Math.abs(v0.z) + Math.abs(v1.z)) / 2;
          const intensity = Math.min(1, segZ / 30);
          const segAvgY = (v0.projY + v1.projY) / 2;
          const sb = scanBoost(segAvgY);
          const segCx = (v0.projX + v1.projX) / 2;
          const dim = centerDim(segCx, segAvgY, screenCx, screenCy);

          const alpha = Math.min(1, (0.035 + intensity * 0.45 + sb * 0.55) * dim);
          const lw = 0.4 + intensity * 1.4 + sb * 1.0;

          const lr = Math.min(255, cr + Math.round(sb * 35));
          const lg = Math.min(255, cg + Math.round(sb * 60));
          const lb = Math.min(255, cb + Math.round(sb * 30));

          ctx.beginPath();
          ctx.strokeStyle = `rgba(${lr},${lg},${lb},${alpha})`;
          ctx.lineWidth = lw;

          if (c === 0) {
            ctx.moveTo(v0.projX, v0.projY);
            ctx.lineTo(v1.projX, v1.projY);
          } else {
            const prev = grid[r][c - 1];
            const mx0 = (prev.projX + v0.projX) / 2, my0 = (prev.projY + v0.projY) / 2;
            const mx1 = (v0.projX + v1.projX) / 2, my1 = (v0.projY + v1.projY) / 2;
            ctx.moveTo(mx0, my0);
            ctx.quadraticCurveTo(v0.projX, v0.projY, mx1, my1);
          }
          ctx.stroke();
        }
      }

      // Vertical grid lines
      for (let c = 0; c < cols; c++) {
        for (let r = 0; r < rows - 1; r++) {
          const v0 = grid[r][c], v1 = grid[r + 1][c];
          const segZ = (Math.abs(v0.z) + Math.abs(v1.z)) / 2;
          const intensity = Math.min(1, segZ / 30);
          const segAvgY = (v0.projY + v1.projY) / 2;
          const sb = scanBoost(segAvgY);
          const segCx = (v0.projX + v1.projX) / 2;
          const dim = centerDim(segCx, segAvgY, screenCx, screenCy);

          const alpha = Math.min(1, (0.035 + intensity * 0.45 + sb * 0.55) * dim);
          const lw = 0.4 + intensity * 1.4 + sb * 1.0;

          const lr = Math.min(255, cr + Math.round(sb * 35));
          const lg = Math.min(255, cg + Math.round(sb * 60));
          const lb = Math.min(255, cb + Math.round(sb * 30));

          ctx.beginPath();
          ctx.strokeStyle = `rgba(${lr},${lg},${lb},${alpha})`;
          ctx.lineWidth = lw;

          if (r === 0) {
            ctx.moveTo(v0.projX, v0.projY);
            ctx.lineTo(v1.projX, v1.projY);
          } else {
            const prev = grid[r - 1][c];
            const mx0 = (prev.projX + v0.projX) / 2, my0 = (prev.projY + v0.projY) / 2;
            const mx1 = (v0.projX + v1.projX) / 2, my1 = (v0.projY + v1.projY) / 2;
            ctx.moveTo(mx0, my0);
            ctx.quadraticCurveTo(v0.projX, v0.projY, mx1, my1);
          }
          ctx.stroke();
        }
      }

      // Glow dots
      for (let r = 0; r < rows; r++) {
        for (let c = 0; c < cols; c++) {
          const v = grid[r][c];
          const sb = scanBoost(v.projY);
          const elevGlow = v.z > 6;
          if (elevGlow || sb > 0.3) {
            const tt = elevGlow ? Math.min(1, v.z / 45) : 0;
            const total = Math.min(1, tt + sb * 0.6);
            const dim = centerDim(v.projX, v.projY, screenCx, screenCy);
            const radius = 1 + total * 2.5;
            ctx.beginPath();
            ctx.arc(v.projX, v.projY, radius, 0, Math.PI * 2);
            const dr = Math.min(255, cr + 40);
            const dg = Math.min(255, cg + 50);
            const db = Math.min(255, cb + 50);
            ctx.fillStyle = `rgba(${dr},${dg},${db},${total * 0.8 * dim})`;
            ctx.fill();
          }
        }
      }

      // Energy lines — straight
      const nearVerts: { px: number; py: number; dist: number; z: number }[] = [];
      for (let r = 0; r < rows; r++) {
        for (let c = 0; c < cols; c++) {
          const v = grid[r][c];
          if (v.z > 5) {
            const ddx = mx - v.projX;
            const ddy = my - v.projY;
            const d = Math.sqrt(ddx * ddx + ddy * ddy);
            if (d < 180 && d > 8) {
              nearVerts.push({ px: v.projX, py: v.projY, dist: d, z: v.z });
            }
          }
        }
      }
      nearVerts.sort((a, b) => a.dist - b.dist);
      nearVerts.slice(0, 10).forEach(nv => {
        const tt = 1 - nv.dist / 180;
        const zf = Math.min(1, nv.z / 30);
        ctx.beginPath();
        ctx.moveTo(mx, my);
        ctx.lineTo(nv.px, nv.py);
        ctx.strokeStyle = `rgba(${cr},${cg},${cb},${tt * 0.12 * zf})`;
        ctx.lineWidth = tt * 5;
        ctx.stroke();
        ctx.beginPath();
        ctx.moveTo(mx, my);
        ctx.lineTo(nv.px, nv.py);
        const lr = Math.min(255, cr + 35);
        const lg = Math.min(255, cg + 40);
        const lb = Math.min(255, cb + 40);
        ctx.strokeStyle = `rgba(${lr},${lg},${lb},${tt * 0.5 * zf})`;
        ctx.lineWidth = tt * 1.8;
        ctx.stroke();
      });

      // Peak glow
      let peakZ = 0, peakX = mx, peakY = my;
      for (let r = 0; r < rows; r++) {
        for (let c = 0; c < cols; c++) {
          const v = grid[r][c];
          if (v.z > peakZ) {
            const dd = Math.sqrt((mx - v.baseX) ** 2 + (my - v.baseY) ** 2);
            if (dd < HOVER_RADIUS) { peakZ = v.z; peakX = v.projX; peakY = v.projY; }
          }
        }
      }
      if (peakZ > 8) {
        const glowR = 60 + peakZ * 1.5;
        const gradient = ctx.createRadialGradient(peakX, peakY, 0, peakX, peakY, glowR);
        gradient.addColorStop(0, `rgba(${cr},${cg},${cb},${Math.min(0.12, peakZ / 300)})`);
        gradient.addColorStop(0.4, `rgba(${cr},${cg},${cb},${Math.min(0.05, peakZ / 700)})`);
        gradient.addColorStop(1, `rgba(${cr},${cg},${cb},0)`);
        ctx.fillStyle = gradient;
        ctx.beginPath();
        ctx.arc(peakX, peakY, glowR, 0, Math.PI * 2);
        ctx.fill();
      }

      frame = requestAnimationFrame(loop);
    };

    frame = requestAnimationFrame(loop);

    return () => {
      running = false;
      window.removeEventListener('resize', buildGrid);
      window.removeEventListener('mousemove', onMove);
      window.removeEventListener('click', onClick);
      cancelAnimationFrame(frame);
    };
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []); // Empty deps — reads theme from ref

  return (
    <canvas ref={canvasRef} className="fixed inset-0 z-0 pointer-events-none" />
  );
}
