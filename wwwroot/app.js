const $ = (id) => document.getElementById(id);
const TRACE_RECORD_BYTES = 20;
const state = {
  algorithms: [],
  array: [],
  initialArray: [],
  sortedArray: [],
  trace: null,
  traceView: null,
  stepCount: 0,
  frameIndex: 0,
  highlightedIndices: [],
  description: "Generate an array to begin.",
  checkpoints: [],
  playing: false,
  playFrameHandle: null,
  playStartTime: 0,
  playStartIndex: 0,
  dataSignature: "",
  audioContext: null,
  audioMasterGain: null,
  audioMinValue: 0,
  audioMaxValue: 1,
  nextSoundTime: 0
};

const canvas = $("sort-canvas");
const context = canvas.getContext("2d");
const numberFormat = new Intl.NumberFormat();

document.querySelectorAll(".tab").forEach((tab) => {
  tab.addEventListener("click", () => {
    document.querySelectorAll(".tab").forEach((item) => item.classList.toggle("active", item === tab));
    $("visualizer-panel").classList.toggle("active", tab.dataset.tab === "visualizer");
    $("visualizer-panel").classList.toggle("hidden", tab.dataset.tab !== "visualizer");
    $("benchmark-panel").classList.toggle("active", tab.dataset.tab === "benchmark");
    $("benchmark-panel").classList.toggle("hidden", tab.dataset.tab !== "benchmark");
    if (tab.dataset.tab === "visualizer") requestAnimationFrame(drawFrame);
  });
});

$("algorithm").addEventListener("change", updateComplexity);
$("data-mode").addEventListener("change", () => $("custom-wrap").classList.toggle("hidden", $("data-mode").value !== "custom"));
$("bench-data-mode").addEventListener("change", () => $("bench-custom-wrap").classList.toggle("hidden", $("bench-data-mode").value !== "custom"));
$("speed").addEventListener("input", syncSpeedNumber);
$("speed-value").addEventListener("input", syncSpeedSlider);
$("speed-value").addEventListener("change", normalizeSpeedNumber);
$("new-data").addEventListener("click", createVisualizerArray);
$("run-sort").addEventListener("click", runSort);
$("restart").addEventListener("click", () => { pausePlayback(); setFrame(0); });
$("previous").addEventListener("click", () => { pausePlayback(); setFrame(state.frameIndex - 1); });
$("next").addEventListener("click", () => { pausePlayback(); setFrame(state.frameIndex + 1); });
$("play").addEventListener("click", () => state.playing ? pausePlayback() : startPlayback());
$("sound-enabled").addEventListener("change", () => {
  if ($("sound-enabled").checked) ensureAudioContext();
});
$("timeline").addEventListener("input", () => { pausePlayback(); setFrame(Number($("timeline").value)); });
$("run-benchmark").addEventListener("click", runBenchmark);
window.addEventListener("resize", drawFrame);
window.addEventListener("keydown", (event) => {
  if (event.target instanceof HTMLInputElement || event.target instanceof HTMLTextAreaElement || event.target instanceof HTMLSelectElement) return;
  if (event.code === "Space" && $("visualizer-panel").classList.contains("active")) {
    event.preventDefault();
    state.playing ? pausePlayback() : startPlayback();
  } else if (event.key === "ArrowLeft" && $("visualizer-panel").classList.contains("active")) {
    pausePlayback(); setFrame(state.frameIndex - 1);
  } else if (event.key === "ArrowRight" && $("visualizer-panel").classList.contains("active")) {
    pausePlayback(); setFrame(state.frameIndex + 1);
  }
});

async function initialize() {
  try {
    const response = await fetch("/api/algorithms");
    if (!response.ok) throw new Error("Could not load the algorithm list.");
    state.algorithms = await response.json();
    $("algorithm").innerHTML = state.algorithms.map((algorithm) =>
      '<option value="' + escapeHtml(algorithm.name) + '">' + escapeHtml(algorithm.name) + "</option>").join("");
    updateComplexity();
    createVisualizerArray();
  } catch (error) {
    setMessage("sort-status", error.message, "error");
  }
}

function updateComplexity() {
  const algorithm = state.algorithms.find((item) => item.name === $("algorithm").value);
  $("complexity").textContent = algorithm
    ? "Time " + algorithm.timeComplexity + "  ·  Space " + algorithm.spaceComplexity
    : "Algorithm details unavailable.";
}

function readValues(modeId, lengthId, customId) {
  const mode = $(modeId).value;
  if (mode === "custom") {
    const text = $(customId).value.trim();
    if (!text) throw new Error("Enter one or more custom values.");
    const tokens = text.split(/[\s,;]+/).filter(Boolean);
    const values = tokens.map((token) => {
      if (!/^[+-]?\d+$/.test(token)) throw new Error('"' + token + '" is not a whole number.');
      const value = Number(token);
      if (!Number.isInteger(value) || value < -2147483648 || value > 2147483647) {
        throw new Error("Values must fit in a 32-bit signed integer.");
      }
      return value;
    });
    return { values, signature: mode + ":" + text };
  }

  const lengthText = $(lengthId).value.trim();
  const length = Number(lengthText);
  if (!/^\d+$/.test(lengthText) || !Number.isSafeInteger(length) || length < 1) {
    throw new Error("Array length must be a positive whole number.");
  }

  let values;
  if (mode === "sorted") {
    values = Array.from({ length }, (_, index) => index);
  } else if (mode === "reversed") {
    values = Array.from({ length }, (_, index) => length - index - 1);
  } else {
    values = Array.from({ length }, () => Math.floor(Math.random() * 1000) + 1);
  }
  return { values, signature: mode + ":" + length + (mode === "random" ? ":" + Math.random() : "") };
}

function resetTrace() {
  state.trace = null;
  state.traceView = null;
  state.stepCount = 0;
  state.frameIndex = 0;
  state.highlightedIndices = [];
  state.description = "Unsorted input array.";
  state.checkpoints = [];
  state.sortedArray = [];
}

function createVisualizerArray() {
  pausePlayback();
  try {
    const generated = readValues("data-mode", "array-length", "custom-values");
    state.initialArray = generated.values;
    updateAudioValueRange();
    state.array = state.initialArray.slice();
    state.dataSignature = currentVisualizerSignature();
    resetTrace();
    updateSpeedLimit();
    clearMetrics();
    setFrame(0);
    $("canvas-title").textContent = "Unsorted input";
    setMessage("sort-status", "Array ready. Choose an algorithm and run it.", "success");
  } catch (error) {
    setMessage("sort-status", error.message, "error");
  }
}

function currentVisualizerSignature() {
  const mode = $("data-mode").value;
  return mode === "custom"
    ? mode + ":" + $("custom-values").value.trim()
    : mode + ":" + $("array-length").value.trim();
}

async function runSort() {
  pausePlayback();
  try {
    const signature = currentVisualizerSignature();
    if (state.initialArray.length === 0 || signature !== state.dataSignature) {
      const generated = readValues("data-mode", "array-length", "custom-values");
      state.initialArray = generated.values;
      state.dataSignature = signature;
    }
    updateAudioValueRange();
    if (state.initialArray.length === 0) return;
    const algorithmName = $("algorithm").value;
    if (!algorithmName) throw new Error("Choose a sorting algorithm.");
    $("run-sort").disabled = true;
    state.array = state.initialArray.slice();
    resetTrace();
    state.array = state.initialArray.slice();
    setFrame(0);
    setMessage("sort-status", "Sorting and recording every operation…", "");
    $("canvas-title").textContent = algorithmName;

    const response = await fetch("/api/sort", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ algorithmName, values: state.initialArray })
    });
    const result = await response.json().catch(() => ({}));
    if (!response.ok) throw new Error(result.error || "Sort request failed.");

    state.trace = await decodeTrace(result.trace);
    if (state.trace.byteLength !== result.stepCount * TRACE_RECORD_BYTES) {
      throw new Error("The operation trace was incomplete. Run the sort again.");
    }
    state.traceView = new DataView(state.trace.buffer, state.trace.byteOffset, state.trace.byteLength);
    state.stepCount = result.stepCount;
    state.sortedArray = result.sortedArray;
    updateSpeedLimit();
    state.array = state.initialArray.slice();
    state.frameIndex = 0;
    state.description = "Unsorted input array.";
    state.checkpoints = buildCheckpoints();
    $("metric-comparisons").textContent = numberFormat.format(result.statistics.comparisons);
    $("metric-swaps").textContent = numberFormat.format(result.statistics.swaps);
    $("metric-writes").textContent = numberFormat.format(result.statistics.writes);
    $("metric-time").textContent = formatDuration(result.elapsedMs);
    setFrame(0);
    setMessage("sort-status", numberFormat.format(state.stepCount) + " individual operations recorded. Playback advances one operation at a time.", "success");
  } catch (error) {
    setMessage("sort-status", error.message, "error");
  } finally {
    $("run-sort").disabled = false;
  }
}

async function decodeTrace(base64) {
  if (typeof DecompressionStream === "undefined") {
    throw new Error("This browser does not support compressed sort playback. Update your browser and try again.");
  }
  const binary = atob(base64);
  const compressed = new Uint8Array(binary.length);
  for (let index = 0; index < binary.length; index++) compressed[index] = binary.charCodeAt(index);
  const stream = new Blob([compressed]).stream().pipeThrough(new DecompressionStream("gzip"));
  return new Uint8Array(await new Response(stream).arrayBuffer());
}

function buildCheckpoints() {
  const count = state.stepCount;
  const arrayLength = Math.max(1, state.initialArray.length);
  const checkpointBudget = Math.max(1, Math.floor(250000 / arrayLength));
  const interval = Math.max(1, Math.ceil(count / checkpointBudget));
  const checkpoints = [{ index: 0, array: state.initialArray.slice() }];
  const working = state.initialArray.slice();

  for (let index = 0; index < count; index++) {
    applyTraceAction(working, index, 1);
    if ((index + 1) % interval === 0 && index + 1 < count) {
      checkpoints.push({ index: index + 1, array: working.slice() });
    }
  }

  checkpoints.push({ index: count, array: state.sortedArray.slice() });
  return checkpoints;
}

function readTraceAction(index) {
  const offset = index * TRACE_RECORD_BYTES;
  return {
    kind: state.traceView.getUint8(offset),
    firstIndex: state.traceView.getInt32(offset + 4, true),
    secondIndex: state.traceView.getInt32(offset + 8, true),
    previousValue: state.traceView.getInt32(offset + 12, true),
    value: state.traceView.getInt32(offset + 16, true)
  };
}

function applyTraceAction(array, index, direction) {
  const action = readTraceAction(index);
  if (action.kind === 2) {
    const value = array[action.firstIndex];
    array[action.firstIndex] = array[action.secondIndex];
    array[action.secondIndex] = value;
  } else if (action.kind === 3) {
    array[action.firstIndex] = direction > 0 ? action.value : action.previousValue;
  }
}

function setFrame(index) {
  if (state.initialArray.length === 0) return;
  const previousFrame = state.frameIndex;
  const target = Math.max(0, Math.min(Math.trunc(index), state.stepCount));
  if (target !== state.frameIndex) {
    const distance = Math.abs(target - state.frameIndex);
    if (distance <= 4096) {
      if (target > state.frameIndex) {
        for (let step = state.frameIndex; step < target; step++) applyTraceAction(state.array, step, 1);
      } else {
        for (let step = state.frameIndex - 1; step >= target; step--) applyTraceAction(state.array, step, -1);
      }
    } else {
      const checkpoint = findCheckpoint(target);
      state.array = checkpoint.array.slice();
      for (let step = checkpoint.index; step < target; step++) applyTraceAction(state.array, step, 1);
    }
  }

  state.frameIndex = target;
  if (target === state.stepCount && state.sortedArray.length) state.array = state.sortedArray.slice();
  $("timeline").max = state.stepCount;
  $("timeline").value = state.frameIndex;
  $("step-label").textContent = "Step " + numberFormat.format(state.frameIndex) + " of " + numberFormat.format(state.stepCount);
  $("array-count").textContent = numberFormat.format(state.array.length) + " ITEMS";
  $("empty-state").classList.add("hidden");

  if (target === 0) {
    state.highlightedIndices = [];
    state.description = "Unsorted input array.";
  } else {
    const action = readTraceAction(target - 1);
    state.highlightedIndices = [action.firstIndex, action.secondIndex].filter((value) => value >= 0);
    state.description = describeAction(action);
  }
  $("step-description").textContent = state.description;
  $("canvas-title").textContent = state.stepCount > 0 && target === state.stepCount
    ? "Sorting complete"
    : ($("algorithm").value || "Ready to sort");
  if (target !== previousFrame && target > 0 && state.traceView) {
    const actionIndex = target > previousFrame ? target - 1 : target;
    playActionSound(readTraceAction(actionIndex));
  }
  drawFrame();
}

function findCheckpoint(target) {
  let low = 0;
  let high = state.checkpoints.length - 1;
  while (low < high) {
    const middle = Math.ceil((low + high) / 2);
    if (state.checkpoints[middle].index <= target) low = middle;
    else high = middle - 1;
  }
  return state.checkpoints[low];
}

function describeAction(action) {
  const first = numberFormat.format(action.previousValue);
  const second = numberFormat.format(action.value);
  switch (action.kind) {
    case 1:
      return action.firstIndex >= 0
        ? "Compare " + first + " and " + second + " at indices " + action.firstIndex + " and " + action.secondIndex + "."
        : "Compare " + first + " with " + second + ".";
    case 2:
      return "Swap " + first + " and " + second + " at indices " + action.firstIndex + " and " + action.secondIndex + ".";
    case 3:
      return "Write " + second + " to index " + action.firstIndex + " (replaced " + first + ").";
    case 4:
      return "Perform an auxiliary-array write.";
    case 5:
      return "Record " + numberFormat.format(action.value) + " auxiliary writes.";
    default:
      return "Sorting operation.";
  }
}

function updateAudioValueRange() {
  if (state.initialArray.length === 0) {
    state.audioMinValue = 0;
    state.audioMaxValue = 1;
    return;
  }

  let minimum = state.initialArray[0];
  let maximum = state.initialArray[0];
  for (let index = 1; index < state.initialArray.length; index++) {
    const value = state.initialArray[index];
    if (value < minimum) minimum = value;
    if (value > maximum) maximum = value;
  }
  state.audioMinValue = minimum;
  state.audioMaxValue = maximum;
}

function ensureAudioContext() {
  const AudioContextType = window.AudioContext || window.webkitAudioContext;
  if (!AudioContextType || !$("sound-enabled").checked) return null;

  if (!state.audioContext) {
    state.audioContext = new AudioContextType();
    state.audioMasterGain = state.audioContext.createGain();
    state.audioMasterGain.gain.value = 0.35;
    state.audioMasterGain.connect(state.audioContext.destination);
  }
  if (state.audioContext.state === "suspended") {
    state.audioContext.resume().catch(() => {});
  }
  return state.audioContext;
}

function playActionSound(action) {
  const audio = ensureAudioContext();
  if (!audio) return;

  const now = audio.currentTime;
  if (now < state.nextSoundTime) return;
  state.nextSoundTime = now + 1 / 36;

  let value = action.value;
  if ((action.kind === 4 || action.kind === 5) && action.firstIndex < 0 && state.array.length > 0) {
    value = state.array[Math.floor(state.array.length / 2)];
  }

  const valueRange = Math.max(1, state.audioMaxValue - state.audioMinValue);
  const normalized = Math.max(0, Math.min(1, (value - state.audioMinValue) / valueRange));
  const pentatonicSemitones = [0, 2, 4, 7, 9, 12, 14, 16, 19, 21, 24, 26, 28, 31, 33, 36];
  const scaleIndex = Math.round(normalized * (pentatonicSemitones.length - 1));
  const midiNote = 48 + pentatonicSemitones[scaleIndex];
  const frequency = 440 * Math.pow(2, (midiNote - 69) / 12);

  const envelope = audio.createGain();
  envelope.gain.setValueAtTime(0.0001, now);
  envelope.gain.exponentialRampToValueAtTime(0.42, now + 0.006);
  envelope.gain.exponentialRampToValueAtTime(0.0001, now + 0.085);
  envelope.connect(state.audioMasterGain);

  const fundamental = audio.createOscillator();
  fundamental.type = "sine";
  fundamental.frequency.setValueAtTime(frequency, now);
  fundamental.connect(envelope);

  const overtone = audio.createOscillator();
  const overtoneGain = audio.createGain();
  overtone.type = "sine";
  overtone.frequency.setValueAtTime(frequency * 2, now);
  overtoneGain.gain.value = 0.12;
  overtone.connect(overtoneGain);
  overtoneGain.connect(envelope);

  fundamental.onended = () => {
    fundamental.disconnect();
    overtone.disconnect();
    overtoneGain.disconnect();
    envelope.disconnect();
  };
  fundamental.start(now);
  overtone.start(now);
  fundamental.stop(now + 0.09);
  overtone.stop(now + 0.09);
}
function automaticSpeedMaximum() {
  return Math.max(120, Math.ceil(state.stepCount / 30));
}

function updateSpeedLimit() {
  const maximum = automaticSpeedMaximum();
  $("speed").max = maximum;

  let rate = Number($("speed-value").value);
  if (!Number.isSafeInteger(rate) || rate < 1) {
    rate = Math.min(30, maximum);
    $("speed-value").value = rate;
  }
  $("speed").value = Math.min(rate, maximum);
}

function updatePlaybackClock() {
  if (!state.playing) return;
  state.playStartIndex = state.frameIndex;
  state.playStartTime = performance.now();
}

function syncSpeedNumber() {
  $("speed-value").value = $("speed").value;
  updatePlaybackClock();
}

function syncSpeedSlider() {
  const rate = Number($("speed-value").value);
  if (!Number.isSafeInteger(rate) || rate < 1) return;
  $("speed").value = Math.min(rate, automaticSpeedMaximum());
  updatePlaybackClock();
}

function normalizeSpeedNumber() {
  const rate = Number($("speed-value").value);
  const normalized = Number.isSafeInteger(rate) && rate > 0
    ? rate
    : Math.min(30, automaticSpeedMaximum());
  $("speed-value").value = normalized;
  $("speed").value = Math.min(normalized, automaticSpeedMaximum());
  updatePlaybackClock();
}

function startPlayback() {
  if (state.stepCount < 1) return;
  if ($("sound-enabled").checked) ensureAudioContext();
  if (state.frameIndex >= state.stepCount) setFrame(0);
  state.playing = true;
  state.playStartIndex = state.frameIndex;
  state.playStartTime = performance.now();
  $("play").textContent = "Ⅱ Pause";
  cancelAnimationFrame(state.playFrameHandle);
  state.playFrameHandle = requestAnimationFrame(advancePlayback);
}

function advancePlayback(now) {
  if (!state.playing) return;
  const rate = Number($("speed-value").value);
  const elapsed = Math.max(0, now - state.playStartTime);
  const target = Math.min(state.stepCount, state.playStartIndex + Math.floor(elapsed * rate / 1000));
  if (target > state.frameIndex) setFrame(target);
  if (state.frameIndex >= state.stepCount) {
    pausePlayback();
    return;
  }
  state.playFrameHandle = requestAnimationFrame(advancePlayback);
}

function pausePlayback() {
  state.playing = false;
  cancelAnimationFrame(state.playFrameHandle);
  $("play").textContent = "▶ Play";
}

function clearMetrics() {
  $("metric-comparisons").textContent = "—";
  $("metric-swaps").textContent = "—";
  $("metric-writes").textContent = "—";
  $("metric-time").textContent = "—";
}

function drawFrame() {
  if (!canvas || !context) return;
  const rect = canvas.getBoundingClientRect();
  const width = Math.max(1, rect.width);
  const height = Math.max(1, rect.height);
  const ratio = Math.max(1, window.devicePixelRatio || 1);
  const pixelWidth = Math.round(width * ratio);
  const pixelHeight = Math.round(height * ratio);
  if (canvas.width !== pixelWidth || canvas.height !== pixelHeight) {
    canvas.width = pixelWidth;
    canvas.height = pixelHeight;
  }
  context.setTransform(ratio, 0, 0, ratio, 0, 0);
  context.clearRect(0, 0, width, height);

  const values = state.array;
  if (!values || values.length === 0) {
    $("empty-state").classList.remove("hidden");
    return;
  }
  $("empty-state").classList.add("hidden");

  const maxBars = Math.max(1, Math.floor(width / 1.6));
  const bucketSize = Math.max(1, Math.ceil(values.length / maxBars));
  const barCount = Math.ceil(values.length / bucketSize);
  const gap = barCount > width / 3 ? 0 : Math.min(2, width / barCount * 0.14);
  const slotWidth = width / barCount;
  let minValue = values[0];
  let maxValue = values[0];
  for (let index = 1; index < values.length; index++) {
    if (values[index] < minValue) minValue = values[index];
    if (values[index] > maxValue) maxValue = values[index];
  }
  const range = Math.max(1, maxValue - minValue);
  const topPadding = 5;
  const usableHeight = height - topPadding - 5;
  const highlights = new Set(state.highlightedIndices);
  context.fillStyle = "#17232e";
  context.fillRect(0, height - 1, width, 1);

  for (let bucket = 0; bucket < barCount; bucket++) {
    const start = bucket * bucketSize;
    const end = Math.min(values.length, start + bucketSize);
    let peak = minValue;
    let highlighted = false;
    for (let index = start; index < end; index++) {
      if (values[index] > peak) peak = values[index];
      if (highlights.has(index)) highlighted = true;
    }
    const normalized = (peak - minValue) / range;
    const barHeight = Math.max(2, normalized * usableHeight);
    const x = bucket * slotWidth + gap / 2;
    const y = height - barHeight;
    context.fillStyle = highlighted ? "#ffb657" : "#42cde0";
    context.fillRect(x, y, Math.max(1, slotWidth - gap), barHeight);
  }
}

async function runBenchmark() {
  try {
    const input = readValues("bench-data-mode", "bench-array-length", "bench-custom-values");
    const button = $("run-benchmark");
    button.disabled = true;
    setMessage("benchmark-status", "Running each algorithm on " + numberFormat.format(input.values.length) + " values…", "");
    $("benchmark-count").textContent = "RUNNING";
    const response = await fetch("/api/benchmark", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ values: input.values })
    });
    const result = await response.json().catch(() => ({}));
    if (!response.ok) throw new Error(result.error || "Benchmark request failed.");
    const sorted = result.results.slice().sort((left, right) => left.elapsedMs - right.elapsedMs);
    $("benchmark-rows").innerHTML = sorted.map((row) =>
      "<tr><td>" + escapeHtml(row.algorithmName) + "</td>" +
      "<td>" + formatDuration(row.elapsedMs) + "</td>" +
      "<td>" + numberFormat.format(row.statistics.comparisons) + "</td>" +
      "<td>" + numberFormat.format(row.statistics.swaps) + "</td>" +
      "<td>" + numberFormat.format(row.statistics.writes) + "</td>" +
      '<td class="complexity-cell">' + escapeHtml(row.timeComplexity) + "</td></tr>").join("");
    $("benchmark-count").textContent = result.results.length + " ALGORITHMS · " + numberFormat.format(result.count) + " ITEMS";
    setMessage("benchmark-status", "Benchmark finished. Times are measured by the sorting engine.", "success");
  } catch (error) {
    setMessage("benchmark-status", error.message, "error");
  } finally {
    $("run-benchmark").disabled = false;
  }
}

function formatDuration(milliseconds) {
  if (milliseconds >= 1000) return (milliseconds / 1000).toFixed(2) + " s";
  if (milliseconds >= 10) return milliseconds.toFixed(2) + " ms";
  return milliseconds.toFixed(3) + " ms";
}

function setMessage(id, message, kind) {
  const element = $(id);
  element.textContent = message;
  element.classList.remove("error", "success");
  if (kind) element.classList.add(kind);
}

function escapeHtml(text) {
  return String(text).replace(/[&<>"']/g, (character) => ({
    "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;"
  })[character]);
}

initialize();


