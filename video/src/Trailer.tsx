import React from "react";
import { Audio } from "@remotion/media";
import { linearTiming, TransitionSeries } from "@remotion/transitions";
import { fade } from "@remotion/transitions/fade";
import { slide } from "@remotion/transitions/slide";
import { AbsoluteFill, interpolate, staticFile } from "remotion";
import { Background, SilentContext } from "./components";
import {
  BuiltInsScene,
  CustomizeScene,
  DURATIONS,
  FeaturesScene,
  IntroScene,
  LaunchScene,
  MenuScene,
  OutroScene,
  QuickMenuScene,
  SafetyScene,
  ThemesScene,
} from "./scenes";
import "./theme";

const TRANSITION = 18;

const scenes: { name: string; duration: number; element: React.ReactNode }[] = [
  { name: "Intro", duration: DURATIONS.intro, element: <IntroScene /> },
  { name: "Menu", duration: DURATIONS.menu, element: <MenuScene /> },
  { name: "Launch", duration: DURATIONS.launch, element: <LaunchScene /> },
  { name: "Customize", duration: DURATIONS.customize, element: <CustomizeScene /> },
  { name: "Quick Menu", duration: DURATIONS.quick, element: <QuickMenuScene /> },
  { name: "Themes", duration: DURATIONS.themes, element: <ThemesScene /> },
  { name: "Built-ins", duration: DURATIONS.builtins, element: <BuiltInsScene /> },
  { name: "Safety", duration: DURATIONS.safety, element: <SafetyScene /> },
  { name: "Features", duration: DURATIONS.features, element: <FeaturesScene /> },
  { name: "Outro", duration: DURATIONS.outro, element: <OutroScene /> },
];

export const TRAILER_DURATION = scenes.reduce((sum, s) => sum + s.duration, 0) - TRANSITION * (scenes.length - 1);

export const Trailer: React.FC<{ silent?: boolean }> = ({ silent = false }) => {
  return (
    <SilentContext.Provider value={silent}>
    <AbsoluteFill>
      <Background />
      {!silent && (
        <Audio
          src={staticFile("audio/ambience.wav")}
          loop
          loopVolumeCurveBehavior="extend"
          volume={(f) => interpolate(f, [0, 40, TRAILER_DURATION - 60, TRAILER_DURATION], [0, 0.55, 0.55, 0], { extrapolateLeft: "clamp", extrapolateRight: "clamp" })}
        />
      )}
      <TransitionSeries>
        {scenes.flatMap((scene, i) => {
          const items = [
            <TransitionSeries.Sequence key={scene.name} name={scene.name} durationInFrames={scene.duration}>
              {scene.element}
            </TransitionSeries.Sequence>,
          ];
          if (i < scenes.length - 1) {
            items.push(
              <TransitionSeries.Transition
                key={`${scene.name}-transition`}
                presentation={i % 3 === 1 ? slide({ direction: "from-right" }) : fade()}
                timing={linearTiming({ durationInFrames: TRANSITION })}
              />,
            );
          }
          return items;
        })}
      </TransitionSeries>
    </AbsoluteFill>
    </SilentContext.Provider>
  );
};
