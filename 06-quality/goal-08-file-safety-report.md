# Goal 08 File Safety Report

The receive boundary rejects relative traversal, slash/backslash components,
absolute/drive paths, NTFS ADS separators, NUL/control characters, `.`/`..`,
and Windows device names including extension variants. Unicode names are
FormKC-normalized and trailing spaces/dots are removed before the receiver
selects a path under its configured root.

Every accepted chunk must match transfer ID, exact index/offset/length and
SHA-256. Resume state is accepted only for the same session, transfer, complete
manifest hash, chunk size, normalized name and unexpired grant. Every retained
chunk is reread and rehashed. The final path remains absent until all chunks
and the streaming whole-file hash match. A mismatch deletes temporary state;
the implementation contains no shell execute or automatic-open call.

The sender rents one chunk buffer and waits for transport capacity for every
chunk. The receiver limits concurrent transfers and checks volume free space
plus reserve before creating the bounded sparse temporary file.

Either peer can cancel all active transfers. Sender cancellation tokens stop
the next read/send boundary without holding the transfer registry lock; the
peer receives a `FileCancel`. Receiver policy either preserves the verified
partial state for bounded resume or deletes it. Permission revocation uses the
same cancellation barrier before the new revision is advertised.

On supported Windows filesystems, the completed file is passed through
Attachment Execution Services and receives Internet-zone metadata. Filesystems
without alternate streams skip only Zone.Identifier; they do not skip hash,
policy or attachment inspection. Controlled Folder Access and commercial
AV/EDR products still require signed-artifact lab evidence before promotion.
