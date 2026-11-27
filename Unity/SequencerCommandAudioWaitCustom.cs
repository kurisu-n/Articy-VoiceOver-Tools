using System.Text.RegularExpressions;
using UnityEngine;

namespace PixelCrushers.DialogueSystem.SequencerCommands
{
    // AudioWait that falls back to the main-language file when a localized one is missing.
    // AudioWait(entrytaglocal) asks for "name_en": if that clip exists it plays, otherwise this plays "name",
    // so a line plays in the main language until its translation is recorded.
    public class SequencerCommandAudioWaitCustom : SequencerCommandAudioWait
    {
        // "_en", "_el", "_de-DE": a language code, optionally with a region
        private static readonly Regex LocaleSuffixRegex = new Regex(@"^(.*)_([A-Za-z]{2,3}(?:-[A-Za-z0-9]{2,4})?)$", RegexOptions.Compiled);

        /// <summary>
        /// Plays the named clip, or the same name without its language suffix when the named clip does not exist.
        /// </summary>
        /// <param name="audioClipNameIn">The clip name the sequence asked for, typically an entrytaglocal value such as "name_en".</param>
        /// <remarks>
        /// A name without a language suffix is passed straight to <see cref="SequencerCommandAudioWait"/>.
        /// The existence check loads the localized clip once and releases it again, so with Addressables it holds no reference of its own.
        /// </remarks>
        protected override void TryAudioClip(string audioClipNameIn)
        {
            if (string.IsNullOrEmpty(audioClipNameIn))
            {
                base.TryAudioClip(audioClipNameIn);
                return;
            }

            var match = LocaleSuffixRegex.Match(audioClipNameIn);
            if (!match.Success)
            {
                base.TryAudioClip(audioClipNameIn);
                return;
            }

            var baseName = match.Groups[1].Value;

            DialogueManager.LoadAsset(audioClipNameIn, typeof(AudioClip), asset =>
            {
                if (asset is AudioClip)
                {
                    base.TryAudioClip(audioClipNameIn);
                }
                else
                {
                    if (DialogueDebug.logInfo)
                        Debug.Log($"{DialogueDebug.Prefix}: AudioWaitCustom: '{audioClipNameIn}' not found, playing '{baseName}' instead.");
                    base.TryAudioClip(baseName);
                }

                // base loads its own reference, so the probe's goes back once that load has started
                if (asset != null)
                    DialogueManager.UnloadAsset(asset);
            });
        }
    }
}
