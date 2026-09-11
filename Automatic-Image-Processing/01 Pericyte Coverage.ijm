// =====================================================================
//  Pericyte Coverage.ijm
//
//  Runs on the image that is currently open in ImageJ / Fiji.
//
//  Part A - clean up and save
//    1. Image  > Stacks > Z Project        (Max Intensity)
//    2. Process > Noise > Remove Outliers   (defaults, all channels)
//    3. Process > Noise > Despeckle         (all channels)
//    4. Image  > Color > Make Composite
//    5. File   > Save As > Tiff...          (into the ONE empty subfolder of parentDir)
//    6. Image  > Color > Split Channels
//    7. File   > Save As > Tiff...  for each channel window (C1-, C2-, ...),
//       saved to the same folder, named exactly like its window
//    The image is kept at its original bit depth (no 8-bit conversion), so the
//    thresholds below depend only on the pixel data, not on the display range.
//
//  Part B - analysis
//    EC channel (C2):       Threshold Auto > Apply, Create Selection, Measure, Enlarge 5 microns
//    Pericyte channel (C1): Threshold Auto > Apply, Restore Selection, Make Inverse,
//                           clear outside (Backspace), Create Selection, Measure
//    EC channel (C2):       Measure
//    Then one line per image is appended to the master sheet (masterFile, a CSV
//    that opens in Excel): Image, EC Area before enlarge, EC Pericyte Overlap,
//    EC Area enlarged.
//
//  To run:  Plugins > Macros > Run...  and pick this file,
//  or drop the file onto the ImageJ toolbar and press Run.
// =====================================================================

// ---- Settings (edit here if anything changes) -----------------------
// The experiment folder. Before running, create ONE new empty folder inside it
// for the image you are about to process - the macro saves into that folder.
parentDir  = "/Users/alexwu/Documents/Duke/02 LAB/01 Confocal Images/20260803_GOx MPS Lectin/";

// Master sheet that every run appends one line to (created with a header row on first use).
// Point this somewhere else, e.g. one folder up, to collect several experiments in one sheet.
masterFile = parentDir + "Measurements.csv";

projection = "Max Intensity";   // Z Project method
outlierRadius    = 2;           // Remove Outliers: Radius
outlierThreshold = 50;          // Remove Outliers: Threshold
outlierWhich     = "Bright";    // Remove Outliers: Which outliers ("Bright" or "Dark")

// Exactly what the Recorder shows when you press "Auto" in Image > Adjust > Threshold:
//   "Default"  = the method in the dialog's drop-down
//   "dark"     = the "Dark background" box is ticked
//   "no-reset" = the "Don't reset range" box is ticked, so the histogram spans the
//                image's current display range rather than its min-max data range
autoThreshold = "Default dark no-reset";
enlargeBy  = 5;                 // EC channel: Enlarge selection by this many microns

// Which channel window holds which stain (the number after "C" in the split-channel windows).
// This batch: C2 = endothelial cells (EC), C1 = pericytes. (The June batch was C3 and C2.)
ecChannel       = 2;
pericyteChannel = 1;
// ---------------------------------------------------------------------

if (nImages == 0)
    exit("No image is open.\nOpen a z-stack first, then run this macro.");

// ---- Find the one empty subfolder to save into -----------------------
if (!File.isDirectory(parentDir))
    exit("Folder not found:\n" + parentDir + "\n\nEdit parentDir at the top of the macro.");
list = getFileList(parentDir);
emptyDirs = newArray(0);
for (i = 0; i < list.length; i++) {
    // getFileList marks subfolders with a trailing "/" and ignores hidden files like .DS_Store
    if (endsWith(list[i], "/")) {
        contents = getFileList(parentDir + list[i]);
        if (contents.length == 0)
            emptyDirs = Array.concat(emptyDirs, list[i]);
    }
}
if (emptyDirs.length == 0)
    exit("No empty folder found in\n" + parentDir + "\n\nCreate a new empty folder for this image, then run the macro again.");
if (emptyDirs.length > 1) {
    names = "";
    for (i = 0; i < emptyDirs.length; i++) names = names + "   " + emptyDirs[i] + "\n";
    exit("More than one empty folder found in\n" + parentDir + "\n" + names
         + "\nLeave only the folder for this image empty, then run the macro again.");
}
saveDir = parentDir + emptyDirs[0];
print("Saving into: " + saveDir);

warnings = "";

// =====================================================================
//  Part A - clean up and save
// =====================================================================

origTitle = getTitle();               // written to the master sheet's "Image" column

// 1. Z Project (each channel is projected separately)
Stack.getDimensions(width, height, channels, slices, frames);
if (slices > 1)
    run("Z Project...", "projection=[" + projection + "]");
else
    print("Note: " + getTitle() + " has only one z-slice, so Z Project was skipped.");

// 2. Remove Outliers  (OK with the values above, then Yes to process all)
//    Done BEFORE Despeckle - the order matters for the resulting pixel values.
run("Remove Outliers...", "radius=" + outlierRadius + " threshold=" + outlierThreshold
    + " which=" + outlierWhich + " stack");

// 3. Despeckle  ("stack" = answer Yes to "Process all images?")
run("Despeckle", "stack");

// (No 8-bit conversion: the image keeps its original bit depth.)

// 4. Make Composite  (skipped for a single-channel image, where ImageJ would throw an error)
Stack.getDimensions(width, height, channels, slices, frames);
if (channels > 1)
    run("Make Composite", "display=Composite");
else
    print("Note: " + getTitle() + " has only one channel, so Make Composite was skipped.");

// 5. Save As Tiff, using the window title (e.g. "MAX_myimage") as the file name
savePath = saveDir + fileNameFromTitle(getTitle()) + ".tif";
saveAs("Tiff", savePath);
print("Saved: " + savePath);

// 6. Split Channels  (skipped for a single-channel image, where ImageJ would throw an error)
compositeTitle = getTitle();          // after Save As this is the file name, e.g. "MAX_image.tif"
if (channels > 1) {
    run("Split Channels");

    // 7. Save every channel window (C1-..., C2-..., ...) as a TIFF with the same name
    for (c = 1; c <= channels; c++) {
        chTitle = "C" + c + "-" + compositeTitle;
        if (isOpen(chTitle)) {
            selectWindow(chTitle);
            chPath = saveDir + fileNameFromTitle(chTitle) + ".tif";
            saveAs("Tiff", chPath);
            print("Saved: " + chPath);
        } else {
            warnings = warnings + "- Window '" + chTitle + "' was not found, so channel " + c + " was NOT saved.\n";
        }
    }
} else {
    print("Note: " + compositeTitle + " has only one channel, so Split Channels was skipped.");
}

// =====================================================================
//  Part B - analysis on the EC channel and the pericyte channel
// =====================================================================
ecWin = "C" + ecChannel + "-" + compositeTitle;          // endothelial cells
pcWin = "C" + pericyteChannel + "-" + compositeTitle;    // pericytes
if (!isOpen(ecWin) || !isOpen(pcWin))
    exit("All files were saved, but the analysis needs the windows\n" + ecWin + "\n" + pcWin
         + "\nand this image has only " + channels + " channel(s).\nCheck ecChannel / pericyteChannel at the top of the macro.");

// ---- EC channel -----------------------------------------------------
selectWindow(ecWin);

// "Enlarge by 5 microns" only works if the image is calibrated in microns
getPixelSize(unit, pixelWidth, pixelHeight);
if (!isMicronUnit(unit))
    exit("All files were saved, but " + ecWin + " is calibrated in '" + unit + "', not microns,\n"
         + "so 'Enlarge by " + enlargeBy + " microns' cannot be done.\nCheck Image > Properties.");

// EC-1. Image > Adjust > Threshold > Auto > Apply   (dark background, i.e. bright objects)
applyAutoThreshold(autoThreshold);
run("Convert to Mask");

// EC-2. Edit > Selection > Create Selection
run("Create Selection");
if (selectionType() == -1)
    exit("All files were saved, but the auto threshold selected no pixels in " + ecWin
         + ",\nso there is nothing to measure or enlarge.");

// EC-3. Analyze > Measure   (row 1: "EC Area before enlarge")
run("Measure");
ecArea = lastArea();

// EC-4. Edit > Selection > Enlarge > 5 microns > OK
run("Enlarge...", "enlarge=" + enlargeBy);

// Make sure the ENLARGED outline is the one "Restore Selection" brings back on the pericyte channel
// (Select None stores the current selection as the one to restore; Restore puts it straight back)
run("Select None");
run("Restore Selection");

// ---- Pericyte channel -----------------------------------------------
selectWindow(pcWin);

// PC-1. Image > Adjust > Threshold > Auto > Apply   (dark background, i.e. bright objects)
applyAutoThreshold(autoThreshold);
run("Convert to Mask");

// PC-2. Edit > Selection > Restore Selection   (the enlarged EC outline)
run("Restore Selection");
if (selectionType() == -1)
    exit("All files were saved, but Restore Selection did not bring the EC outline over to " + pcWin + ".");

// PC-3. Edit > Selection > Make Inverse
run("Make Inverse");

// PC-4. Backspace = clear the selection to background.
//       Done by setting the pixels to 0 (the background value of a mask), which is what Backspace
//       does here but without depending on the background colour in Edit > Options > Colors.
if (selectionType() != -1)
    run("Set...", "value=0");
else
    print("Note: the enlarged EC outline covers the whole image, so nothing was cleared in " + pcWin + ".");

// PC-5. Edit > Selection > Create Selection
run("Create Selection");

// PC-6. Analyze > Measure   (row 2: "EC Pericyte Overlap")
if (selectionType() == -1) {
    warnings = warnings + "- No " + pcWin + " pixels inside the enlarged EC zone: the pericyte measurement was skipped and written as 0.\n";
    pcArea = 0;
} else {
    run("Measure");
    pcArea = lastArea();
}

// ---- Back to the EC channel -----------------------------------------
selectWindow(ecWin);

// EC-5. Analyze > Measure   (row 3: "EC Area enlarged" - the enlarged zone, still selected on the EC channel)
run("Measure");
zoneArea = lastArea();

// ---- Append one line to the master sheet ----------------------------
if (!File.exists(masterFile))
    File.append("Image,EC Area before enlarge,EC Pericyte Overlap,EC Area enlarged", masterFile);
File.append(csv(origTitle) + "," + d2s(ecArea, 4) + "," + d2s(pcArea, 4) + "," + d2s(zoneArea, 4),
            masterFile);
print("Added a line to the master sheet: " + masterFile);

if (warnings != "")
    showMessage("Macro finished with warnings", warnings);
else
    showStatus("Done: " + compositeTitle);

// ---------------------------------------------------------------------
// Turns a window title into a file name (without extension):
// strips a short extension like .tif / .czi / .lif and replaces characters
// that are not allowed in file names.
function fileNameFromTitle(title) {
    dot = lastIndexOf(title, ".");
    if (dot > 0 && lengthOf(title) - dot <= 5)
        title = substring(title, 0, dot);
    title = replace(title, "/", "_");
    title = replace(title, ":", "_");
    return title;
}

// Presses "Auto" in the Threshold dialog with the options in autoThreshold (this is the very
// command the Recorder writes when you press that button), and notes the result in the Log.
function applyAutoThreshold(options) {
    setAutoThreshold(options);
    getThreshold(lower, upper);
    print(getTitle() + ": auto threshold (" + options + ") = " + lower + "-" + upper);
}

// Area of the measurement that was just added to the Results table. If "Area" is not
// switched on in Analyze > Set Measurements, it is computed directly from the selection.
function lastArea() {
    a = getResult("Area", nResults - 1);
    if (isNaN(a))
        getStatistics(a);
    return a;
}

// Wraps a text value in quotes for the CSV file (so commas in image names are safe)
function csv(s) {
    q = fromCharCode(34);
    s = replace(s, q, q + q);
    return q + s + q;
}

// True if a calibration unit means micrometres: "micron", "microns", "micrometer", "um", "µm"
function isMicronUnit(unit) {
    u = toLowerCase(unit);
    if (startsWith(u, "micro")) return true;
    if (u == "um") return true;
    if (lengthOf(u) == 2 && endsWith(u, "m")) {
        code = charCodeAt(u, 0);
        if (code == 181 || code == 956) return true;    // micro sign / Greek mu
    }
    return false;
}
