"""Original procedural SFX; no recordings, samples, or external dependencies.
Generated assets may be used, modified and redistributed with this project.
Run from the Unity project root with Python 3.
"""
import math
import random
import struct
import wave
from pathlib import Path

RATE = 44100
ROOT = Path('Assets/CombatFeedback/Audio')
ROOT.mkdir(parents=True, exist_ok=True)

def write(name, seconds, sample):
    rng = random.Random(711)
    values = [sample(i / RATE, rng.uniform(-1, 1)) for i in range(int(RATE * seconds))]
    peak = max(abs(v) for v in values)
    data = b''.join(struct.pack('<h', int(28000 * v / peak)) for v in values)
    with wave.open(str(ROOT / (name + '.wav')), 'wb') as out:
        out.setparams((1, 2, RATE, 0, 'NONE', 'not compressed'))
        out.writeframes(data)

def metal(t, noise):
    impact = noise * math.exp(-t * 75) * .65
    body = math.sin(2 * math.pi * (96*t + 1.5*(1-math.exp(-t*45)))) * math.exp(-t*12)
    modes = sum(math.sin(2*math.pi*f*t) * math.exp(-t*d) * a
                for f,d,a in [(413,9,.25),(731,12,.2),(1187,15,.14),(2071,20,.1)])
    return (impact + body + modes) * min(1, t*2000)

def sword(t, noise):
    click = noise * math.exp(-t*105)
    bright = sum(math.sin(2*math.pi*f*t)*a for f,a in [(1620,.4),(2731,.3),(4417,.14)]) * math.exp(-t*24)
    return (click*.7 + bright) * min(1,t*4000)

def emotion(t, noise):
    envelope = (1-math.exp(-t*75))*math.exp(-t*6) * min(1, max(0,(.9-t)*20))
    phase = 2*math.pi*(280*t + 35*math.sin(t*5))
    return envelope * (math.sin(phase)*.65 + math.sin(phase*1.5)*.22 + math.sin(phase*2.01)*.13 + noise*.025)

write('MetalImpact', .65, metal)
write('SwordHit', .36, sword)
write('EmotionalInfluence', .9, emotion)
print('Created three original mono 44.1 kHz PCM WAV sound effects.')
