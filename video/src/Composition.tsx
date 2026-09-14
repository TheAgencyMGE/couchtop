import { Composition, Still } from "remotion";
import { SocialPreview } from "./SocialPreview";
import { Trailer, TRAILER_DURATION } from "./Trailer";

export const MyComposition = () => {
  return (
    <>
      <Composition
        id="CouchtopTrailer"
        component={Trailer}
        durationInFrames={TRAILER_DURATION}
        fps={30}
        width={1920}
        height={1080}
        defaultProps={{ silent: false }}
      />
      <Composition
        id="CouchtopTrailerSilent"
        component={Trailer}
        durationInFrames={TRAILER_DURATION}
        fps={30}
        width={1920}
        height={1080}
        defaultProps={{ silent: true }}
      />
      <Still id="SocialPreview" component={SocialPreview} width={1280} height={640} />
    </>
  );
};
