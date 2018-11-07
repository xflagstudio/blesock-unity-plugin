namespace BleSock
{
    public class Player
    {
        // Properties

        public int PlayerId
        {
            get
            {
                return mPlayerId;
            }
        }

        public string PlayerName
        {
            get
            {
                return mPlayerName;
            }
        }

        public object LocalData { get; set; }

        // Constructor

        internal Player(int playerId, string playerName)
        {
            mPlayerId = playerId;
            mPlayerName = playerName;
        }

        // Internal

        private int mPlayerId;
        private string mPlayerName;
    }
}
