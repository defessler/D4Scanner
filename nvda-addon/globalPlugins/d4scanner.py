# D4Scanner — NVDA global plugin.
#
# Logs the text NVDA is asked to speak to %LOCALAPPDATA%\d4scanner\d4_tts.log.
# When Diablo IV's "Use 3rd Party Screen Reader" routes its tooltip text to NVDA
# (via Tolk -> nvdaControllerClient64.dll -> NVDA speech), this captures the exact
# same strings the saapi64.dll shim would — but with NO file in the Diablo IV folder
# and no custom signed DLL. D4Scanner's parser / live app tail this log unchanged.
#
# Tip: to stay silent while still logging, set NVDA's synthesizer to "No speech"
# (NVDA menu -> Preferences -> Settings -> Speech -> Synthesizer). The speak() call
# still fires (so we still capture) but produces no audio.

import os
import threading

import globalPluginHandler
import speech

_LOG = os.path.join(
    os.environ.get("LOCALAPPDATA", os.path.expanduser("~")),
    "d4scanner", "d4_tts.log",
)
_lock = threading.Lock()


def _text_of(sequence):
    """Join the plain-string parts of an NVDA speech sequence (skip command objects)."""
    try:
        return "".join(part for part in sequence if isinstance(part, str)).strip()
    except Exception:
        return ""


def _write(text):
    if not text:
        return
    try:
        with _lock:
            with open(_LOG, "a", encoding="utf-8") as f:
                f.write(text + "\n")
    except Exception:
        pass


class GlobalPlugin(globalPluginHandler.GlobalPlugin):
    def __init__(self, *args, **kwargs):
        super(GlobalPlugin, self).__init__(*args, **kwargs)
        try:
            os.makedirs(os.path.dirname(_LOG), exist_ok=True)
        except Exception:
            pass
        # Monkey-patch speech.speech.speak (the same hook the Speech Logger add-on uses).
        # speakText() and nvdaController_speakText both route through this module-level
        # function, so reassigning it captures Diablo IV's tooltip text.
        self._orig_speak = None
        try:
            self._orig_speak = speech.speech.speak
            orig = self._orig_speak

            def new_speak(*args, **kwargs):
                seq = args[0] if args else (kwargs.get("speechSequence") or kwargs.get("sequence"))
                _write(_text_of(seq or []))
                return orig(*args, **kwargs)

            speech.speech.speak = new_speak
        except Exception:
            self._orig_speak = None

    def terminate(self, *args, **kwargs):
        try:
            if self._orig_speak is not None:
                speech.speech.speak = self._orig_speak
        finally:
            super(GlobalPlugin, self).terminate(*args, **kwargs)
