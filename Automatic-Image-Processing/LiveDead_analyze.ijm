// ============================================================
// Live/Dead FULL pipeline: preprocess -> save -> split -> count
// Run with the RAW multi-channel Z-stack open as the active image.
// (LiveDead_preprocess.ijm is the save-only version; this one adds
//  thresholding + Analyze Particles on C3 and C1.)
// ============================================================

// Capture the original file name BEFORE processing renames the window.
// Read it off the active image itself (not File.nameWithoutExtension, which
// returns the last-opened file -- i.e. this macro when run via Macros > Run).
origName = getInfo("image.filename");
if (origName == "") origName = getTitle();
dotIndex = lastIndexOf(origName, ".");
if (dotIndex > 0) origName = substring(origName, 0, dotIndex);

// Output folder: the ONE empty subfolder of the attached directory.
// If there isn't exactly one empty subfolder, fall back to the subfolder
// whose name is most similar to the raw file name.
parentDir = "/Users/alexwu/Documents/Duke/02 LAB/01 Confocal Images/20260708 GOx Dead Live" + File.separator;
entries = getFileList(parentDir);

emptyDirs = newArray(0);
subDirs   = newArray(0);
for (e = 0; e < lengthOf(entries); e++) {
    ename = entries[e];
    if (endsWith(ename, "/")) {                    // it's a subfolder
        subDirs = Array.concat(subDirs, ename);
        inner = getFileList(parentDir + ename);
        nContents = 0;                             // count non-hidden contents
        for (j = 0; j < lengthOf(inner); j++)
            if (!startsWith(inner[j], ".")) nContents++;
        if (nContents == 0) emptyDirs = Array.concat(emptyDirs, ename);
    }
}

if (lengthOf(emptyDirs) == 1) {
    outDir = parentDir + emptyDirs[0];             // getFileList names keep the trailing "/"
} else {
    // Fallback: subfolder whose name best matches the raw file name
    fileKey = normalizeName(origName);
    bestScore = -1; bestDir = "";
    for (s = 0; s < lengthOf(subDirs); s++) {
        score = lcsLength(fileKey, normalizeName(subDirs[s]));
        if (score > bestScore) { bestScore = score; bestDir = subDirs[s]; }
    }
    if (bestDir == "") exit("No subfolder found in:\n" + parentDir);
    outDir = parentDir + bestDir;
}

// 1. Image > Stacks > Z Project  (max intensity across the Z-stack)
run("Z Project...", "projection=[Max Intensity]");

// 2. Process > Noise > Despeckle  ("Yes" = process all channels/slices)
run("Despeckle", "stack");

// 3. Process > Noise > Remove Outliers  ("OK" defaults, "Yes" = whole stack)
run("Remove Outliers...", "radius=2 threshold=50 which=Bright stack");

// 4. Image > Type > 8-bit
run("8-bit");

// 5. Image > Color > Make Composite
run("Make Composite");

// 6. File > Save As > Tiff  (into the empty output folder, original name)
saveAs("Tiff", outDir + origName + ".tif");

// 7. Image > Color > Split Channels
compTitle = getTitle();                 // saved composite's window name (ends in .tif)
run("Split Channels");

// 8. Save each channel (C1, C2, C3) as Tiff into the same folder,
//    using each channel window's own name as the file name.
prefixes = newArray("C1-", "C2-", "C3-");
for (i = 0; i < prefixes.length; i++) {
    winName = prefixes[i] + compTitle;  // e.g. "C1-Stiff LiveDead 3 1001.tif"
    selectWindow(winName);
    saveAs("Tiff", outDir + winName);   // title already ends in .tif -> no double extension
}

// ---------- Particle analysis ----------
c1 = "C1-" + compTitle;                  // live / blue
c3 = "C3-" + compTitle;                  // dead / red

// 9. Close the C2 window (not used for live/dead counting)
selectWindow("C2-" + compTitle);
close();

// 10. C3 (dead): Threshold (Otsu, dark background) -> Apply -> Analyze Particles
selectWindow(c3);
setOption("BlackBackground", true);
setAutoThreshold("Otsu dark");           // "Otsu" method + Auto, bright objects on dark bg
run("Convert to Mask");                  // = the "Apply" button
run("Watershed");                        // Process > Binary > Watershed (split touching nuclei)
run("Analyze Particles...", "size=60-Infinity summarize");

// 11. C1 (live): Threshold (Otsu, dark background) -> Apply -> Analyze Particles
selectWindow(c1);
setOption("BlackBackground", true);
setAutoThreshold("Otsu dark");
run("Convert to Mask");
run("Watershed");                        // Process > Binary > Watershed (split touching nuclei)
run("Analyze Particles...", "size=40-Infinity summarize");

// ---------- Remove dead-cell overlap from the live channel ----------
// 12. C3 (dead mask): Edit > Selection > Create Selection
selectWindow(c3);
run("Create Selection");

// 13. C1 (live mask): restore that ROI, invert it, delete
selectWindow(c1);
run("Restore Selection");        // Edit > Selection > Restore Selection
run("Make Inverse");             // Edit > Selection > Make Inverse
run("Clear", "slice");           // Backspace/Delete -> clears selection to background

// 14. C1: rebuild the selection and re-count
run("Create Selection");         // Edit > Selection > Create Selection
run("Analyze Particles...", "size=40-Infinity summarize");

// ---------- Helper functions (used to pick the output folder) ----------
// Lowercase and strip everything except letters/digits, so "Stiff LiveDead 3 1001"
// and "20260625_Stiff_LiveDead_3_1001" become comparable.
function normalizeName(s) {
    s = toLowerCase(s);
    s = replace(s, "[^a-z0-9]", "");
    return s;
}

// Length of the longest common substring of a and b (simple similarity score).
function lcsLength(a, b) {
    best = 0;
    na = lengthOf(a); nb = lengthOf(b);
    for (i = 0; i < na; i++) {
        for (j = 0; j < nb; j++) {
            k = 0;
            while (i + k < na && j + k < nb && substring(a, i+k, i+k+1) == substring(b, j+k, j+k+1))
                k++;
            if (k > best) best = k;
        }
    }
    return best;
}
