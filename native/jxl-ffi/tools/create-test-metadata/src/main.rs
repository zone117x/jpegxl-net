// Tool for creating JXL test files with metadata boxes.
// This tool manipulates the JXL container format (ISO BMFF boxes) to insert
// EXIF, XML/XMP, and JUMBF metadata boxes.

use std::env;
use std::fs;
use std::io;

/// JXL container box types
const BOX_TYPE_FTYP: &[u8; 4] = b"ftyp";
const BOX_TYPE_JXLC: &[u8; 4] = b"jxlc";
const BOX_TYPE_JXLP: &[u8; 4] = b"jxlp";
const BOX_TYPE_EXIF: &[u8; 4] = b"Exif";
const BOX_TYPE_XML: &[u8; 4] = b"xml ";
const BOX_TYPE_JUMB: &[u8; 4] = b"jumb";
const BOX_TYPE_BROB: &[u8; 4] = b"brob";

/// JXL signature bytes
const JXL_SIGNATURE: &[u8; 12] = b"\x00\x00\x00\x0CJXL \x0D\x0A\x87\x0A";

/// Represents a parsed box
#[derive(Debug, Clone)]
struct Box {
    box_type: [u8; 4],
    data: Vec<u8>,
}

impl Box {
    fn new(box_type: [u8; 4], data: Vec<u8>) -> Self {
        Self { box_type, data }
    }

    /// Serialize the box to bytes
    fn to_bytes(&self) -> Vec<u8> {
        let payload_len = self.data.len();
        let total_len = 8 + payload_len;

        let mut result = Vec::with_capacity(total_len);

        if total_len <= u32::MAX as usize {
            // Normal box
            result.extend_from_slice(&(total_len as u32).to_be_bytes());
            result.extend_from_slice(&self.box_type);
            result.extend_from_slice(&self.data);
        } else {
            // Extended size box
            result.extend_from_slice(&1u32.to_be_bytes()); // length = 1 means extended
            result.extend_from_slice(&self.box_type);
            result.extend_from_slice(&((16 + payload_len) as u64).to_be_bytes());
            result.extend_from_slice(&self.data);
        }

        result
    }
}

/// Parse boxes from JXL container data
fn parse_boxes(data: &[u8]) -> io::Result<Vec<Box>> {
    let mut boxes = Vec::new();
    let mut offset = 0;

    while offset < data.len() {
        if offset + 8 > data.len() {
            break;
        }

        let length = u32::from_be_bytes([
            data[offset],
            data[offset + 1],
            data[offset + 2],
            data[offset + 3],
        ]) as usize;

        let box_type: [u8; 4] = [
            data[offset + 4],
            data[offset + 5],
            data[offset + 6],
            data[offset + 7],
        ];

        let (header_size, box_length) = if length == 1 {
            // Extended size
            if offset + 16 > data.len() {
                return Err(io::Error::new(
                    io::ErrorKind::InvalidData,
                    "Truncated extended size box",
                ));
            }
            let ext_length = u64::from_be_bytes([
                data[offset + 8],
                data[offset + 9],
                data[offset + 10],
                data[offset + 11],
                data[offset + 12],
                data[offset + 13],
                data[offset + 14],
                data[offset + 15],
            ]) as usize;
            (16, ext_length)
        } else if length == 0 {
            // Box extends to end of file
            (8, data.len() - offset)
        } else {
            (8, length)
        };

        if offset + box_length > data.len() {
            return Err(io::Error::new(
                io::ErrorKind::InvalidData,
                format!(
                    "Box extends beyond file: offset={}, length={}, file_size={}",
                    offset,
                    box_length,
                    data.len()
                ),
            ));
        }

        let payload_start = offset + header_size;
        let payload_end = offset + box_length;
        let payload = data[payload_start..payload_end].to_vec();

        boxes.push(Box::new(box_type, payload));
        offset += box_length;
    }

    Ok(boxes)
}

/// Check if data is a JXL container (vs bare codestream)
fn is_jxl_container(data: &[u8]) -> bool {
    data.len() >= 12 && &data[..12] == JXL_SIGNATURE
}

/// Check if data is a bare JXL codestream
fn is_bare_codestream(data: &[u8]) -> bool {
    // Bare codestream starts with 0xFF0A
    data.len() >= 2 && data[0] == 0xFF && data[1] == 0x0A
}

/// Wrap a bare codestream in a container
fn wrap_in_container(codestream: &[u8]) -> Vec<u8> {
    let mut result = Vec::new();

    // JXL signature box
    result.extend_from_slice(JXL_SIGNATURE);

    // ftyp box
    let ftyp_data = b"jxl \x00\x00\x00\x00jxl ";
    let ftyp_box = Box::new(*BOX_TYPE_FTYP, ftyp_data.to_vec());
    result.extend_from_slice(&ftyp_box.to_bytes());

    // jxlc box (full codestream)
    let jxlc_box = Box::new(*BOX_TYPE_JXLC, codestream.to_vec());
    result.extend_from_slice(&jxlc_box.to_bytes());

    result
}

/// Create a minimal EXIF box with a TIFF header
fn create_minimal_exif(content: &str) -> Vec<u8> {
    // EXIF data starts with 4-byte TIFF offset (usually 0)
    let mut data = vec![0u8; 4];

    // TIFF header (little-endian)
    data.extend_from_slice(b"II");   // Little-endian byte order
    data.extend_from_slice(&42u16.to_le_bytes()); // TIFF magic
    data.extend_from_slice(&8u32.to_le_bytes());  // IFD0 offset

    // IFD0 with one entry (ImageDescription)
    data.extend_from_slice(&1u16.to_le_bytes()); // Entry count

    // Tag: ImageDescription (0x010E)
    data.extend_from_slice(&0x010Eu16.to_le_bytes()); // Tag
    data.extend_from_slice(&2u16.to_le_bytes());      // Type = ASCII
    let content_bytes = content.as_bytes();
    let count = content_bytes.len() as u32 + 1; // +1 for null terminator
    data.extend_from_slice(&count.to_le_bytes());     // Count

    if count <= 4 {
        // Value fits in offset field
        let mut value = [0u8; 4];
        value[..content_bytes.len()].copy_from_slice(content_bytes);
        data.extend_from_slice(&value);
    } else {
        // Value offset (after IFD)
        let value_offset = 8 + 2 + 12 + 4; // TIFF header + count + entry + next IFD
        data.extend_from_slice(&(value_offset as u32).to_le_bytes());
    }

    // Next IFD offset (0 = none)
    data.extend_from_slice(&0u32.to_le_bytes());

    // Value data (if not inline)
    if count > 4 {
        data.extend_from_slice(content_bytes);
        data.push(0); // Null terminator
    }

    data
}

/// Create a minimal XMP box
fn create_minimal_xmp(content: &str) -> Vec<u8> {
    format!(
        r#"<?xml version="1.0" encoding="UTF-8"?>
<x:xmpmeta xmlns:x="adobe:ns:meta/">
  <rdf:RDF xmlns:rdf="http://www.w3.org/1999/02/22-rdf-syntax-ns#">
    <rdf:Description rdf:about="">
      <dc:description xmlns:dc="http://purl.org/dc/elements/1.1/">{}</dc:description>
    </rdf:Description>
  </rdf:RDF>
</x:xmpmeta>"#,
        content
    ).into_bytes()
}

/// Create a minimal JUMBF box
fn create_minimal_jumbf(content: &str) -> Vec<u8> {
    // JUMBF is a superbox containing description and content boxes
    // For testing, we create a simple structure
    let mut data = Vec::new();

    // jumd (description) box
    let mut jumd_data = Vec::new();
    // UUID for test content type
    jumd_data.extend_from_slice(&[0u8; 16]); // Placeholder UUID
    jumd_data.push(0x03); // Toggles: requestable
    let label = content.as_bytes();
    jumd_data.extend_from_slice(label);
    jumd_data.push(0); // Null terminator

    let jumd_len = (8 + jumd_data.len()) as u32;
    data.extend_from_slice(&jumd_len.to_be_bytes());
    data.extend_from_slice(b"jumd");
    data.extend_from_slice(&jumd_data);

    // json box with content
    let json_content = format!(r#"{{"test": "{}"}}"#, content);
    let json_bytes = json_content.as_bytes();
    let json_len = (8 + json_bytes.len()) as u32;
    data.extend_from_slice(&json_len.to_be_bytes());
    data.extend_from_slice(b"json");
    data.extend_from_slice(json_bytes);

    data
}

/// Compress data using brotli
fn brotli_compress(data: &[u8]) -> Vec<u8> {
    let mut output = Vec::new();
    let params = brotli::enc::BrotliEncoderParams::default();
    brotli::BrotliCompress(&mut std::io::Cursor::new(data), &mut output, &params)
        .expect("Brotli compression failed");
    output
}

/// Create a brob (Brotli-compressed) box wrapping another box type.
/// The brob box format is: [4-byte inner type][brotli-compressed data]
fn create_brob_box(inner_type: &[u8; 4], uncompressed_data: &[u8]) -> Box {
    let compressed = brotli_compress(uncompressed_data);
    let mut content = Vec::with_capacity(4 + compressed.len());
    content.extend_from_slice(inner_type);
    content.extend_from_slice(&compressed);
    Box::new(*BOX_TYPE_BROB, content)
}

fn print_usage() {
    eprintln!("Usage: create-test-metadata <input.jxl> <output.jxl> [options]");
    eprintln!();
    eprintln!("Options:");
    eprintln!("  --exif <content>    Add EXIF box with specified content (can repeat)");
    eprintln!("  --xml <content>     Add XML/XMP box with specified content (can repeat)");
    eprintln!("  --jumbf <content>   Add JUMBF box with specified content (can repeat)");
    eprintln!("  --exif-file <path>  Add EXIF box from file (can repeat)");
    eprintln!("  --xml-file <path>   Add XML box from file (can repeat)");
    eprintln!("  --jumbf-file <path> Add JUMBF box from file (can repeat)");
    eprintln!("  --brotli            Enable brotli compression (brob) for following metadata boxes");
    eprintln!("  --no-brotli         Disable brotli compression (default)");
    eprintln!();
    eprintln!("Examples:");
    eprintln!("  create-test-metadata input.jxl output.jxl --exif 'test1' --exif 'test2'");
    eprintln!("  create-test-metadata input.jxl output.jxl --brotli --exif 'compressed'");
    eprintln!("  create-test-metadata input.jxl output.jxl --exif 'plain' --brotli --exif 'compressed'");
}

fn main() -> io::Result<()> {
    let args: Vec<String> = env::args().collect();

    if args.len() < 3 {
        print_usage();
        std::process::exit(1);
    }

    let input_path = &args[1];
    let output_path = &args[2];

    // Parse options
    // Each entry is (data, use_brotli)
    let mut exif_boxes: Vec<(Vec<u8>, bool)> = Vec::new();
    let mut xml_boxes: Vec<(Vec<u8>, bool)> = Vec::new();
    let mut jumbf_boxes: Vec<(Vec<u8>, bool)> = Vec::new();
    let mut use_brotli = false;

    let mut i = 3;
    while i < args.len() {
        match args[i].as_str() {
            "--brotli" => {
                use_brotli = true;
            }
            "--no-brotli" => {
                use_brotli = false;
            }
            "--exif" => {
                i += 1;
                if i >= args.len() {
                    eprintln!("Error: --exif requires a content argument");
                    std::process::exit(1);
                }
                exif_boxes.push((create_minimal_exif(&args[i]), use_brotli));
            }
            "--xml" => {
                i += 1;
                if i >= args.len() {
                    eprintln!("Error: --xml requires a content argument");
                    std::process::exit(1);
                }
                xml_boxes.push((create_minimal_xmp(&args[i]), use_brotli));
            }
            "--jumbf" => {
                i += 1;
                if i >= args.len() {
                    eprintln!("Error: --jumbf requires a content argument");
                    std::process::exit(1);
                }
                jumbf_boxes.push((create_minimal_jumbf(&args[i]), use_brotli));
            }
            "--exif-file" => {
                i += 1;
                if i >= args.len() {
                    eprintln!("Error: --exif-file requires a path argument");
                    std::process::exit(1);
                }
                exif_boxes.push((fs::read(&args[i])?, use_brotli));
            }
            "--xml-file" => {
                i += 1;
                if i >= args.len() {
                    eprintln!("Error: --xml-file requires a path argument");
                    std::process::exit(1);
                }
                xml_boxes.push((fs::read(&args[i])?, use_brotli));
            }
            "--jumbf-file" => {
                i += 1;
                if i >= args.len() {
                    eprintln!("Error: --jumbf-file requires a path argument");
                    std::process::exit(1);
                }
                jumbf_boxes.push((fs::read(&args[i])?, use_brotli));
            }
            "--help" | "-h" => {
                print_usage();
                std::process::exit(0);
            }
            _ => {
                eprintln!("Unknown option: {}", args[i]);
                print_usage();
                std::process::exit(1);
            }
        }
        i += 1;
    }

    // Read input file
    let input_data = fs::read(input_path)?;
    println!("Read {} bytes from {}", input_data.len(), input_path);

    // Determine if container or bare codestream
    let container_data = if is_jxl_container(&input_data) {
        println!("Input is JXL container format");
        input_data
    } else if is_bare_codestream(&input_data) {
        println!("Input is bare codestream, wrapping in container");
        wrap_in_container(&input_data)
    } else {
        eprintln!("Error: Input is not a valid JXL file");
        std::process::exit(1);
    };

    // Parse boxes
    let mut boxes = parse_boxes(&container_data)?;
    println!("Parsed {} boxes", boxes.len());

    // Find insertion point (after ftyp, before jxlc/jxlp)
    let mut insert_index = 1; // After JXL signature box (index 0)
    for (idx, b) in boxes.iter().enumerate() {
        if b.box_type == *BOX_TYPE_FTYP {
            insert_index = idx + 1;
        } else if b.box_type == *BOX_TYPE_JXLC || b.box_type == *BOX_TYPE_JXLP {
            break;
        }
    }

    // Insert metadata boxes
    let mut new_boxes = Vec::new();
    for (data, compress) in &exif_boxes {
        let new_box = if *compress {
            create_brob_box(BOX_TYPE_EXIF, data)
        } else {
            Box::new(*BOX_TYPE_EXIF, data.clone())
        };
        new_boxes.push(new_box);
        println!(
            "Added {} EXIF box ({} bytes uncompressed)",
            if *compress { "brob-wrapped" } else { "uncompressed" },
            data.len()
        );
    }
    for (data, compress) in &xml_boxes {
        let new_box = if *compress {
            create_brob_box(BOX_TYPE_XML, data)
        } else {
            Box::new(*BOX_TYPE_XML, data.clone())
        };
        new_boxes.push(new_box);
        println!(
            "Added {} XML box ({} bytes uncompressed)",
            if *compress { "brob-wrapped" } else { "uncompressed" },
            data.len()
        );
    }
    for (data, compress) in &jumbf_boxes {
        let new_box = if *compress {
            create_brob_box(BOX_TYPE_JUMB, data)
        } else {
            Box::new(*BOX_TYPE_JUMB, data.clone())
        };
        new_boxes.push(new_box);
        println!(
            "Added {} JUMBF box ({} bytes uncompressed)",
            if *compress { "brob-wrapped" } else { "uncompressed" },
            data.len()
        );
    }

    // Insert new boxes at the insertion point
    for (i, new_box) in new_boxes.into_iter().enumerate() {
        boxes.insert(insert_index + i, new_box);
    }

    // Serialize output
    let mut output_data = Vec::new();
    for b in &boxes {
        // First box is special (JXL signature, written as-is)
        if output_data.is_empty() && b.box_type == [0x4A, 0x58, 0x4C, 0x20] {
            // "JXL " - part of signature
            output_data.extend_from_slice(JXL_SIGNATURE);
        } else {
            output_data.extend_from_slice(&b.to_bytes());
        }
    }

    // Write output
    fs::write(output_path, &output_data)?;
    println!("Wrote {} bytes to {}", output_data.len(), output_path);

    Ok(())
}
