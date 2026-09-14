import React from "react";
import { AbsoluteFill, Img, staticFile } from "remotion";
import { Background } from "./components";
import { ACCENT, BORDER, FONT, SUBTLE, TEXT } from "./theme";

/** 1280x640 image for GitHub's social preview and link cards. */
export const SocialPreview: React.FC = () => {
  return (
    <AbsoluteFill>
      <Background />
      <AbsoluteFill style={{ flexDirection: "row", alignItems: "center", padding: "0 64px", gap: 56 }}>
        <div style={{ display: "flex", flexDirection: "column", gap: 18, width: 470, flexShrink: 0 }}>
          <Img src={staticFile("icon.png")} style={{ width: 108, height: 108, filter: "drop-shadow(0 14px 24px rgba(26,154,211,0.35))" }} />
          <div style={{ fontFamily: FONT, fontWeight: 800, fontSize: 92, lineHeight: 1, color: TEXT }}>
            Couch<span style={{ color: ACCENT }}>top</span>
          </div>
          <div style={{ fontFamily: FONT, fontWeight: 700, fontSize: 36, lineHeight: 1.2, color: SUBTLE }}>
            Turn your Windows PC into a couch console.
          </div>
          <div
            style={{
              alignSelf: "flex-start",
              marginTop: 6,
              fontFamily: FONT,
              fontWeight: 700,
              fontSize: 24,
              color: "#FFFFFF",
              padding: "9px 22px",
              borderRadius: 40,
              background: `linear-gradient(180deg, #5CC8F5, ${ACCENT})`,
            }}
          >
            Very early beta · free &amp; open source
          </div>
        </div>
        <div
          style={{
            width: 660,
            height: (660 * 9) / 16,
            flexShrink: 0,
            borderRadius: 18,
            padding: 6,
            background: "linear-gradient(180deg, #FFFFFF, #EEF2F4)",
            boxShadow: `0 26px 50px rgba(40,80,110,0.26), 0 0 0 3px ${BORDER}`,
            rotate: "2deg",
          }}
        >
          <Img src={staticFile("shots/01-menu.png")} style={{ width: "100%", height: "100%", borderRadius: 13, objectFit: "cover" }} />
        </div>
      </AbsoluteFill>
    </AbsoluteFill>
  );
};
