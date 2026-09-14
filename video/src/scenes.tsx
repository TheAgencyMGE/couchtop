import React from "react";
import { AbsoluteFill, Easing, Img, interpolate, staticFile, useCurrentFrame } from "remotion";
import { Accent, Column, Headline, Pill, Screen, Sfx, Subline } from "./components";
import { ACCENT, BORDER, clamp, FONT, SUBTLE, TEXT } from "./theme";

const ease = Easing.bezier(0.16, 1, 0.3, 1);
const pop = Easing.bezier(0.34, 1.56, 0.64, 1);

export const DURATIONS = {
  intro: 120,
  menu: 180,
  launch: 150,
  customize: 150,
  quick: 150,
  themes: 165,
  builtins: 180,
  safety: 210,
  features: 150,
  outro: 165,
} as const;

export const IntroScene: React.FC = () => {
  const frame = useCurrentFrame();
  return (
    <Column gap={30}>
      <Sfx file="startup" at={4} volume={0.9} />
      <Img
        src={staticFile("icon.png")}
        style={{
          width: 230,
          height: 230,
          filter: "drop-shadow(0 24px 40px rgba(26,154,211,0.35))",
          scale: interpolate(frame, [0, 26], [0.2, 1], { ...clamp, easing: pop }),
          rotate: interpolate(frame, [0, 26], ["-18deg", "0deg"], { ...clamp, easing: ease }),
          opacity: interpolate(frame, [0, 10], [0, 1], clamp),
        }}
      />
      <Headline delay={14} size={180}>
        Couch<Accent>top</Accent>
      </Headline>
      <Subline delay={30} size={54}>
        Turn your Windows PC into a couch console.
      </Subline>
      <div style={{ marginTop: 16 }}>
        <Pill delay={52} size={36} strong>
          v0.1.0 · very early beta
        </Pill>
      </div>
    </Column>
  );
};

export const MenuScene: React.FC = () => (
  <Column>
    <Sfx file="page" at={2} volume={0.6} />
    <Sfx file="hover" at={70} volume={0.9} />
    <Headline>
      Every app becomes a <Accent>channel</Accent>
    </Headline>
    <Screen src="shots/01-menu.png" width={1320} duration={DURATIONS.menu} />
  </Column>
);

export const LaunchScene: React.FC = () => {
  const frame = useCurrentFrame();
  return (
    <AbsoluteFill>
      <Column>
        <Sfx file="select" at={40} />
        <Sfx file="launch" at={98} volume={0.7} />
        <Headline>
          Point, click, <Accent>Start</Accent>
        </Headline>
        <Screen src="shots/03-preview.png" width={1320} duration={DURATIONS.launch} zoom={[1, 1.08]} />
      </Column>
      <AbsoluteFill style={{ background: "#FFFFFF", opacity: interpolate(frame, [98, 106, 132], [0, 0.85, 0], clamp) }} />
    </AbsoluteFill>
  );
};

export const CustomizeScene: React.FC = () => (
  <Column>
    <Sfx file="page" at={2} volume={0.6} />
    <Sfx file="tick" at={60} volume={1} />
    <Sfx file="select" at={96} volume={0.7} />
    <Headline>
      Drag, rename &amp; <Accent>recolor</Accent>
    </Headline>
    <Screen src="shots/02-customize.png" width={1320} duration={DURATIONS.customize} />
  </Column>
);

export const QuickMenuScene: React.FC = () => (
  <Column>
    <Sfx file="homeopen" at={30} volume={0.9} />
    <Headline>
      A <Accent>Quick Menu</Accent> over any app
    </Headline>
    <Screen src="shots/11-home-menu.png" width={1320} duration={DURATIONS.quick} />
  </Column>
);

export const ThemesScene: React.FC = () => {
  const frame = useCurrentFrame();
  const width = 1320;
  const reveal = interpolate(frame, [40, 110], [100, 0], { ...clamp, easing: Easing.bezier(0.65, 0, 0.35, 1) });
  return (
    <Column>
      <Sfx file="page" at={40} volume={0.7} />
      <Headline>
        Classic by day, <Accent>Night</Accent> by night
      </Headline>
      <div style={{ position: "relative", width, height: (width * 9) / 16 }}>
        <Screen src="shots/01-menu.png" width={width} duration={DURATIONS.themes} zoom={[1, 1]} style={{ position: "absolute", inset: 0 }} />
        <div style={{ position: "absolute", inset: 0, clipPath: `inset(0 ${reveal}% 0 0)` }}>
          <Screen src="shots/12-night.png" width={width} duration={DURATIONS.themes} zoom={[1, 1]} delay={0} />
        </div>
        <div
          style={{
            position: "absolute",
            top: -20,
            bottom: -20,
            width: 8,
            borderRadius: 4,
            left: `${100 - reveal}%`,
            background: ACCENT,
            boxShadow: "0 0 30px rgba(79,208,255,0.9)",
            opacity: interpolate(frame, [36, 42, 108, 118], [0, 1, 1, 0], clamp),
          }}
        />
      </div>
    </Column>
  );
};

export const BuiltInsScene: React.FC = () => {
  const cards = [
    { src: "shots/07-files.png", label: "Files" },
    { src: "shots/09-power.png", label: "Power" },
    { src: "shots/04-settings.png", label: "Settings" },
  ];
  return (
    <Column gap={60}>
      <Sfx file="page" at={2} volume={0.6} />
      <Headline>
        Built-in channels, <Accent>ready to go</Accent>
      </Headline>
      <div style={{ display: "flex", gap: 44, alignItems: "flex-start" }}>
        {cards.map((card, i) => (
          <div key={card.src} style={{ display: "flex", flexDirection: "column", alignItems: "center", gap: 30 }}>
            <Screen src={card.src} width={560} duration={DURATIONS.builtins} delay={14 + i * 12} zoom={[1.02, 1.12]} />
            <Pill delay={34 + i * 12} size={38}>
              {card.label}
            </Pill>
          </div>
        ))}
      </div>
      <Sfx file="tick" at={34} />
      <Sfx file="tick" at={46} />
      <Sfx file="tick" at={58} />
    </Column>
  );
};

export const SafetyScene: React.FC = () => {
  const frame = useCurrentFrame();
  const items = [
    "Crash recovery back to Explorer",
    "Freeze & boot-loop protection",
    "One-key emergency exit",
    "Standalone recovery tool",
    "Uninstall restores Explorer first",
  ];
  return (
    <Column gap={56}>
      <Sfx file="page" at={2} volume={0.6} />
      <Headline>
        Shell mode with a <Accent>safety net</Accent>
      </Headline>
      <div style={{ display: "flex", gap: 70, alignItems: "center" }}>
        <div style={{ display: "flex", flexDirection: "column", gap: 30, width: 760 }}>
          {items.map((item, i) => {
            const d = 24 + i * 16;
            return (
              <div
                key={item}
                style={{
                  display: "flex",
                  alignItems: "center",
                  gap: 26,
                  opacity: interpolate(frame, [d, d + 12], [0, 1], clamp),
                  translate: interpolate(frame, [d, d + 22], ["-50px 0px", "0px 0px"], { ...clamp, easing: ease }),
                }}
              >
                <div
                  style={{
                    width: 58,
                    height: 58,
                    flexShrink: 0,
                    borderRadius: "50%",
                    background: "linear-gradient(180deg, #7FD86A, #5DBB3F)",
                    color: "white",
                    display: "flex",
                    alignItems: "center",
                    justifyContent: "center",
                    fontFamily: FONT,
                    fontWeight: 800,
                    fontSize: 36,
                    boxShadow: "0 8px 20px rgba(93,187,63,0.35)",
                    scale: interpolate(frame, [d + 4, d + 20], [0.3, 1], { ...clamp, easing: pop }),
                  }}
                >
                  ✓
                </div>
                <div style={{ fontFamily: FONT, fontWeight: 700, fontSize: 42, color: TEXT, whiteSpace: "nowrap" }}>{item}</div>
              </div>
            );
          })}
        </div>
        <Screen src="shots/06-safety-test.png" width={880} duration={DURATIONS.safety} delay={12} />
      </div>
      {[0, 1, 2, 3, 4].map((i) => (
        <Sfx key={i} file="tick" at={28 + i * 16} />
      ))}
    </Column>
  );
};

export const FeaturesScene: React.FC = () => {
  const features = ["Game controllers", "Steam & Epic games", "Multi-monitor & DPI", "Original art & music", "No telemetry", "Free & open source"];
  return (
    <Column gap={70}>
      <Sfx file="page" at={2} volume={0.6} />
      <Headline size={110}>
        Made for the <Accent>couch</Accent>
      </Headline>
      <div style={{ display: "flex", flexWrap: "wrap", justifyContent: "center", gap: 34, maxWidth: 1500 }}>
        {features.map((f, i) => (
          <Pill key={f} delay={18 + i * 8} size={50}>
            {f}
          </Pill>
        ))}
      </div>
      {features.map((f, i) => (
        <Sfx key={f} file="hover" at={20 + i * 8} volume={0.7} />
      ))}
    </Column>
  );
};

export const OutroScene: React.FC = () => {
  const frame = useCurrentFrame();
  return (
    <Column gap={34}>
      <Sfx file="launch" at={6} volume={0.8} />
      <Img
        src={staticFile("icon.png")}
        style={{
          width: 190,
          height: 190,
          filter: "drop-shadow(0 20px 36px rgba(26,154,211,0.35))",
          scale: interpolate(frame, [0, 24], [0.3, 1], { ...clamp, easing: pop }),
          opacity: interpolate(frame, [0, 8], [0, 1], clamp),
        }}
      />
      <Headline delay={10} size={150}>
        Couch<Accent>top</Accent>
      </Headline>
      <Subline delay={24} size={50}>
        Very early beta · free &amp; open source
      </Subline>
      <div
        style={{
          marginTop: 20,
          fontFamily: FONT,
          fontWeight: 700,
          fontSize: 48,
          color: ACCENT,
          padding: "22px 54px",
          borderRadius: 60,
          background: "rgba(255,255,255,0.85)",
          boxShadow: `0 0 0 3px ${BORDER}, 0 16px 40px rgba(40,80,110,0.14)`,
          opacity: interpolate(frame, [40, 56], [0, 1], clamp),
          translate: interpolate(frame, [40, 64], ["0px 30px", "0px 0px"], { ...clamp, easing: ease }),
        }}
      >
        github.com/TheAgencyMGE/couchtop
      </div>
      <div style={{ fontFamily: FONT, fontWeight: 500, fontSize: 28, color: SUBTLE, opacity: interpolate(frame, [60, 76], [0, 1], clamp) }}>
        Independent project · original art, sound &amp; code
      </div>
    </Column>
  );
};
