import { createContext, useContext, useState, useRef, useCallback, useMemo, type ReactNode } from 'react';

export interface ThemeState {
  hex: string;
  r: number;
  g: number;
  b: number;
  title: string;
  blurEnabled: boolean;
  funFact: string;
}

interface ThemeContextType {
  theme: ThemeState;
  setHex: (hex: string) => void;
  setTitle: (title: string) => void;
  setBlurEnabled: (v: boolean) => void;
  setFunFact: (f: string) => void;
  themeRef: React.RefObject<ThemeState>;
}

function hexToRgb(hex: string) {
  const h = hex.replace('#', '');
  return {
    r: parseInt(h.substring(0, 2), 16) || 220,
    g: parseInt(h.substring(2, 4), 16) || 38,
    b: parseInt(h.substring(4, 6), 16) || 38,
  };
}

const defaultTheme: ThemeState = {
  hex: '#dc2626', r: 220, g: 38, b: 38,
  title: 'NEXUS',
  blurEnabled: false,
  funFact: '',
};

const ThemeContext = createContext<ThemeContextType>({
  theme: defaultTheme,
  setHex: () => {},
  setTitle: () => {},
  setBlurEnabled: () => {},
  setFunFact: () => {},
  themeRef: { current: defaultTheme },
});

export function ThemeProvider({ children }: { children: ReactNode }) {
  const [theme, setTheme] = useState<ThemeState>(defaultTheme);
  const themeRef = useRef<ThemeState>(defaultTheme);

  // Update ref whenever state changes
  themeRef.current = theme;

  const setHex = useCallback((hex: string) => {
    const { r, g, b } = hexToRgb(hex);
    setTheme(prev => {
      const next = { ...prev, hex, r, g, b };
      themeRef.current = next;
      return next;
    });
  }, []);

  const setTitle = useCallback((title: string) => {
    setTheme(prev => {
      const next = { ...prev, title };
      themeRef.current = next;
      return next;
    });
  }, []);

  const setBlurEnabled = useCallback((v: boolean) => {
    setTheme(prev => {
      const next = { ...prev, blurEnabled: v };
      themeRef.current = next;
      return next;
    });
  }, []);

  const setFunFact = useCallback((f: string) => {
    setTheme(prev => {
      const next = { ...prev, funFact: f };
      themeRef.current = next;
      return next;
    });
  }, []);

  // Memoize the context value so consumers only re-render when theme actually changes
  const value = useMemo(() => ({
    theme, setHex, setTitle, setBlurEnabled, setFunFact, themeRef,
  }), [theme, setHex, setTitle, setBlurEnabled, setFunFact]);

  return (
    <ThemeContext.Provider value={value}>
      {children}
    </ThemeContext.Provider>
  );
}

export const useTheme = () => useContext(ThemeContext);
