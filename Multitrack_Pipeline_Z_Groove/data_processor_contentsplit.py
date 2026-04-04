import os
import glob
import random
import numpy as np
import mido
import pickle
from collections import defaultdict

POSITIONS_PER_BAR = 128

def parse_and_normalize_midi(file_path):
    try:
        mid = mido.MidiFile(file_path)
    except:
        return None

    # 1. Strict 4/4 Filter
    for track in mid.tracks:
        for msg in track:
            if msg.type == 'time_signature':
                if msg.numerator != 4 or msg.denominator != 4:
                    return None

    ticks_per_quarter = mid.ticks_per_beat
    ticks_per_bar = ticks_per_quarter * 4
    ticks_per_pos = ticks_per_bar / POSITIONS_PER_BAR

    parsed_events = []
    for track in mid.tracks:
        current_ticks = 0
        for msg in track:
            current_ticks += msg.time
            abs_pos = int(round(current_ticks / ticks_per_pos))
            if msg.type == 'program_change':
                parsed_events.append((abs_pos, 2, msg.channel, msg.program))
            elif msg.type == 'note_on' and msg.velocity > 0:
                parsed_events.append((abs_pos, 1, msg.channel, msg.note))
            elif msg.type == 'note_off' or (msg.type == 'note_on' and msg.velocity == 0):
                parsed_events.append((abs_pos, 0, msg.channel, msg.note))

    parsed_events.sort(key=lambda x: x[0])
    channel_programs = {i: 0 for i in range(16)}
    channel_programs[9] = 128
    open_notes = {}
    closed_notes = []
    for ev in parsed_events:
        abs_pos, ev_type, channel, val1 = ev
        if ev_type == 2:
            if channel != 9: channel_programs[channel] = val1
        elif ev_type == 1:
            key = (channel, val1)
            if key not in open_notes: open_notes[key] = abs_pos
        elif ev_type == 0:
            key = (channel, val1)
            if key in open_notes:
                start_pos = open_notes[key]
                duration = abs_pos - start_pos
                if duration <= 0: duration = 1
                closed_notes.append({
                    'program': channel_programs[channel],
                    'pitch': val1,
                    'abs_onset': start_pos,
                    'duration': duration
                })
                del open_notes[key]
    return closed_notes

def zig_zag_sort(closed_notes):
    if not closed_notes: return []
    max_onset = max(n['abs_onset'] for n in closed_notes)
    total_bars = (max_onset // POSITIONS_PER_BAR) + 1
    bars = [defaultdict(list) for _ in range(total_bars)]
    for note in closed_notes:
        bar_idx = note['abs_onset'] // POSITIONS_PER_BAR
        local_onset = note['abs_onset'] % POSITIONS_PER_BAR
        dur = min(POSITIONS_PER_BAR - local_onset, note['duration'])
        bars[bar_idx][note['program']].append({
            'local_onset': local_onset, 'pitch': note['pitch'], 'duration': dur
        })

    sorted_sequence = []
    for bar_groups in bars:
        if not bar_groups: continue
        track_orders = []
        for prog, notes in bar_groups.items():
            avg_pitch = -1 if prog == 128 else sum(n['pitch'] for n in notes) / len(notes)
            track_orders.append((avg_pitch, prog, notes))
        track_orders.sort(key=lambda x: x[0], reverse=True)
        for _, prog, notes in track_orders:
            sorted_sequence.append(f"i-{prog}")
            notes.sort(key=lambda x: (x['local_onset'], -x['pitch']))
            for n in notes:
                pitch_val = n['pitch'] + (128 if prog == 128 else 0)
                sorted_sequence.append(f"o-{min(127, n['local_onset'])}")
                sorted_sequence.append(f"p-{pitch_val}")
                sorted_sequence.append(f"d-{min(127, n['duration'])}")
        sorted_sequence.append("b-1")
    return sorted_sequence
