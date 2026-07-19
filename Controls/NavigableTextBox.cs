using System.Windows.Forms;

namespace PBIPortWrapper.Controls
{
    /// <summary>
    /// A custom TextBox that properly handles navigation keys (Left, Right, Home, End)
    /// preventing container controls (like DataGridView) from intercepting them.
    /// </summary>
    public class NavigableTextBox : TextBox
    {
        protected override bool IsInputKey(Keys keyData)
        {
            Keys key = keyData & Keys.KeyCode;

            if (key == Keys.Left || 
                key == Keys.Right || 
                key == Keys.Up || 
                key == Keys.Down ||
                key == Keys.Home || 
                key == Keys.End)
            {
                return true;
            }

            return base.IsInputKey(keyData);
        }
    }
}
