namespace SMBLibrary
{
    /// <summary>
    /// Standard access mask combinations for common permission scenarios
    /// </summary>
    public static class StandardAccessMasks
    {
        /// <summary>
        /// Full control - all permissions
        /// Value: 0x001F01FF
        /// </summary>
        public static readonly AccessMask FullControl = (AccessMask)0x001F01FF;
        
        /// <summary>
        /// Modify - read, write, delete, execute (cannot change permissions or owner)
        /// Value: 0x001301BF
        /// </summary>
        public static readonly AccessMask Modify = (AccessMask)0x001301BF;
        
        /// <summary>
        /// Read and execute
        /// Value: 0x001200A9
        /// </summary>
        public static readonly AccessMask ReadAndExecute = (AccessMask)0x001200A9;
        
        /// <summary>
        /// Read only
        /// Value: 0x00120089
        /// </summary>
        public static readonly AccessMask ReadOnly = (AccessMask)0x00120089;
        
        /// <summary>
        /// Write only (rarely used)
        /// Value: 0x00100116
        /// </summary>
        public static readonly AccessMask WriteOnly = (AccessMask)0x00100116;
        
        /// <summary>
        /// Standard read permissions group
        /// Includes: READ_DATA, READ_EA, READ_ATTRIBUTES, READ_CONTROL, SYNCHRONIZE
        /// </summary>
        public static readonly AccessMask ReadPermissions = 
            (AccessMask)FileAccessMask.FILE_READ_DATA |
            (AccessMask)FileAccessMask.FILE_READ_EA |
            (AccessMask)FileAccessMask.FILE_READ_ATTRIBUTES |
            AccessMask.READ_CONTROL |
            AccessMask.SYNCHRONIZE;
        
        /// <summary>
        /// Standard write permissions group
        /// Includes: WRITE_DATA, APPEND_DATA, WRITE_EA, WRITE_ATTRIBUTES, DELETE, WRITE_DAC, WRITE_OWNER
        /// </summary>
        public static readonly AccessMask WritePermissions = 
            (AccessMask)FileAccessMask.FILE_WRITE_DATA |
            (AccessMask)FileAccessMask.FILE_APPEND_DATA |
            (AccessMask)FileAccessMask.FILE_WRITE_EA |
            (AccessMask)FileAccessMask.FILE_WRITE_ATTRIBUTES |
            AccessMask.DELETE |
            AccessMask.WRITE_DAC |
            AccessMask.WRITE_OWNER;
        
        /// <summary>
        /// Execute permission
        /// </summary>
        public static readonly AccessMask ExecutePermission = 
            (AccessMask)FileAccessMask.FILE_EXECUTE;
        
        /// <summary>
        /// Build access mask from simple read/write/delete permissions
        /// </summary>
        public static AccessMask Build(bool canRead, bool canWrite, bool canDelete = true, bool canExecute = true)
        {
            AccessMask mask = 0;
            
            if (canRead)
            {
                mask |= ReadPermissions;
            }
            
            if (canWrite)
            {
                mask |= WritePermissions;
            }
            
            if (!canDelete && canWrite)
            {
                // Remove delete permissions if explicitly disabled
                mask &= ~AccessMask.DELETE;
            }
            
            if (canExecute)
            {
                mask |= ExecutePermission;
            }
            
            return mask;
        }
    }
}

