using UnityEngine;

namespace ChessTheBetrayal.Infrastructure
{
    /// <summary>
    /// Asks the platform for the frame rate the board is animated against.
    ///
    /// Nothing used to ask, and asking for nothing is not the same as asking for the display's best:
    /// with vertical sync off, Unity reads an unset target as "the platform default", and on a phone
    /// that default is 30. Half the frames means every moving thing jumps twice as far between the
    /// ones that do get drawn, which is where a long glide stops looking like motion and starts
    /// looking like a row of stills — and none of it shows up on a desktop editor running free.
    ///
    /// Runs off <see cref="RuntimeInitializeOnLoadMethod"/> rather than from a component in the
    /// scene. A frame rate that only applies when somebody remembered to drag an object into a
    /// scene is a frame rate that will eventually be missing from one, and this project has already
    /// shipped a build missing a visual for exactly that reason.
    /// </summary>
    public static class DisplayFrameRate
    {
        /// <summary>
        /// Sixty rather than whatever the panel can do. A 120Hz phone would happily draw a chessboard
        /// at 120 and halve its own battery life doing it; the board has nothing that moves fast
        /// enough to tell the difference, since the pacing curve deliberately keeps every glide well
        /// inside what 60 can show.
        /// </summary>
        private const int TargetFramesPerSecond = 60;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        public static void Apply()
        {
            Application.targetFrameRate = TargetFramesPerSecond;
        }
    }
}
