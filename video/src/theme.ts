import { loadFont } from "@remotion/fonts";
import { staticFile } from "remotion";

export const FONT = "CouchtopRounded";
export const ACCENT = "#35B4E5";
export const ACCENT_DEEP = "#1A9AD3";
export const TEXT = "#4E5C64";
export const SUBTLE = "#8A969C";
export const BORDER = "#C4CCD1";

export const clamp = { extrapolateLeft: "clamp", extrapolateRight: "clamp" } as const;

loadFont({ family: FONT, url: staticFile("fonts/MPLUSRounded1c-Medium.ttf"), weight: "500" });
loadFont({ family: FONT, url: staticFile("fonts/MPLUSRounded1c-Bold.ttf"), weight: "700" });
loadFont({ family: FONT, url: staticFile("fonts/MPLUSRounded1c-ExtraBold.ttf"), weight: "800" });
