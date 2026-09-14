import React from "react";
import { Audio } from "@remotion/media";
import { AbsoluteFill, Easing, Img, interpolate, Sequence, staticFile, useCurrentFrame } from "remotion";
import { ACCENT, BORDER, clamp, FONT, SUBTLE, TEXT } from "./theme";

const ease = Easing.bezier(0.16, 1, 0.3, 1);
const pop = Easing.bezier(0.34, 1.56, 0.64, 1);

export const Background: React.FC = () => {
  const frame = useCurrentFrame();
  return (
    <AbsoluteFill style={{ background: "linear-gradient(180deg, #FFFFFF 0%, #F3F7F9 55%, #E3EBEF 100%)" }}>
      <AbsoluteFill
        style={{
          backgroundImage: "repeating-linear-gradient(180deg, rgba(90,106,115,0.05) 0px, rgba(90,106,115,0.05) 1px, transparent 1px, transparent 7px)",
        }}
      />
      <div
        style={{
          position: "absolute",
          width: 1100,
          height: 1100,
          borderRadius: "50%",
          background: "radial-gradient(circle, rgba(79,208,255,0.22) 0%, rgba(79,208,255,0) 65%)",
          left: -300,
          top: -420,
          translate: `${interpolate(frame, [0, 600], [0, 180])}px ${interpolate(frame, [0, 600], [0, 90])}px`,
        }}
      />
      <div
        style={{
          position: "absolute",
          width: 900,
          height: 900,
          borderRadius: "50%",
          background: "radial-gradient(circle, rgba(255,180,210,0.16) 0%, rgba(255,180,210,0) 65%)",
          right: -260,
          bottom: -380,
          translate: `${interpolate(frame, [0, 600], [0, -140])}px ${interpolate(frame, [0, 600], [0, -60])}px`,
        }}
      />
    </AbsoluteFill>
  );
};

export const Headline: React.FC<{ children: React.ReactNode; delay?: number; size?: number }> = ({ children, delay = 0, size = 92 }) => {
  const frame = useCurrentFrame();
  return (
    <div
      style={{
        fontFamily: FONT,
        fontWeight: 800,
        fontSize: size,
        lineHeight: 1.08,
        color: TEXT,
        textAlign: "center",
        opacity: interpolate(frame, [delay, delay + 16], [0, 1], clamp),
        translate: interpolate(frame, [delay, delay + 26], ["0px 46px", "0px 0px"], { ...clamp, easing: ease }),
      }}
    >
      {children}
    </div>
  );
};

export const Accent: React.FC<{ children: React.ReactNode }> = ({ children }) => <span style={{ color: ACCENT }}>{children}</span>;

export const Subline: React.FC<{ children: React.ReactNode; delay?: number; size?: number }> = ({ children, delay = 8, size = 46 }) => {
  const frame = useCurrentFrame();
  return (
    <div
      style={{
        fontFamily: FONT,
        fontWeight: 500,
        fontSize: size,
        color: SUBTLE,
        textAlign: "center",
        opacity: interpolate(frame, [delay, delay + 20], [0, 1], clamp),
        translate: interpolate(frame, [delay, delay + 28], ["0px 30px", "0px 0px"], { ...clamp, easing: ease }),
      }}
    >
      {children}
    </div>
  );
};

/** A screenshot presented like a glossy TV screen that rises in and slowly zooms. */
export const Screen: React.FC<{
  src: string;
  width: number;
  duration: number;
  delay?: number;
  zoom?: [number, number];
  style?: React.CSSProperties;
}> = ({ src, width, duration, delay = 6, zoom = [1, 1.06], style }) => {
  const frame = useCurrentFrame();
  const height = (width * 9) / 16;
  return (
    <div
      style={{
        width,
        height,
        borderRadius: width * 0.024,
        padding: Math.max(6, width * 0.007),
        background: "linear-gradient(180deg, #FFFFFF, #EEF2F4)",
        boxShadow: `0 ${width * 0.035}px ${width * 0.07}px rgba(40,80,110,0.26), 0 0 0 3px ${BORDER}`,
        opacity: interpolate(frame, [delay, delay + 14], [0, 1], clamp),
        scale: interpolate(frame, [delay, delay + 30], [0.9, 1], { ...clamp, easing: ease }),
        translate: interpolate(frame, [delay, delay + 30], ["0px 70px", "0px 0px"], { ...clamp, easing: ease }),
        ...style,
      }}
    >
      <div style={{ width: "100%", height: "100%", borderRadius: width * 0.017, overflow: "hidden" }}>
        <Img
          src={staticFile(src)}
          style={{
            width: "100%",
            height: "100%",
            objectFit: "cover",
            scale: interpolate(frame, [0, duration], zoom, clamp),
          }}
        />
      </div>
    </div>
  );
};

export const Pill: React.FC<{ children: React.ReactNode; delay?: number; size?: number; strong?: boolean }> = ({ children, delay = 0, size = 40, strong = false }) => {
  const frame = useCurrentFrame();
  return (
    <div
      style={{
        fontFamily: FONT,
        fontWeight: 700,
        fontSize: size,
        color: strong ? "#FFFFFF" : TEXT,
        padding: `${size * 0.36}px ${size * 0.9}px`,
        borderRadius: size * 2,
        background: strong ? `linear-gradient(180deg, #5CC8F5, ${ACCENT})` : "linear-gradient(180deg, #FFFFFF, #E7ECEF)",
        boxShadow: strong ? "0 12px 30px rgba(53,180,229,0.35)" : `0 10px 26px rgba(40,80,110,0.12), 0 0 0 3px ${BORDER}`,
        opacity: interpolate(frame, [delay, delay + 12], [0, 1], clamp),
        scale: interpolate(frame, [delay, delay + 22], [0.6, 1], { ...clamp, easing: pop }),
        whiteSpace: "nowrap",
      }}
    >
      {children}
    </div>
  );
};

/** When true, no audio elements are rendered (used for the silent GIF preview). */
export const SilentContext = React.createContext(false);

export const Sfx: React.FC<{ file: string; at: number; volume?: number }> = ({ file, at, volume = 0.8 }) => {
  const silent = React.useContext(SilentContext);
  if (silent) return null;
  return (
    <Sequence from={at} layout="none" name={`sfx ${file}`}>
      <Audio src={staticFile(`audio/${file}.wav`)} volume={volume} />
    </Sequence>
  );
};

export const Column: React.FC<{ children: React.ReactNode; gap?: number }> = ({ children, gap = 44 }) => (
  <AbsoluteFill style={{ alignItems: "center", justifyContent: "center", flexDirection: "column", gap, padding: "100px 120px" }}>
    {children}
  </AbsoluteFill>
);
